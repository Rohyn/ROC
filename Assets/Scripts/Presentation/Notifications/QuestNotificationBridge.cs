using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owner-local observer that turns replicated quest journal snapshots into
/// generic progress notices.
///
/// Future bridges for skills, reputation, titles, and achievements should feed
/// the same PlayerNotificationController progress channel.
/// </summary>
[DisallowMultipleComponent]
public class QuestNotificationBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerNotificationController notificationController;

    [Header("Display")]
    [SerializeField] private bool showQuestAccepted = true;
    [SerializeField] private bool showQuestProgress = true;
    [SerializeField] private bool showQuestReadyToTurnIn = true;
    [SerializeField] private bool showQuestCompleted = true;

    [Header("Timing")]
    [Tooltip("Short debounce lets quest journal snapshot changes settle before diffing.")]
    [SerializeField] private float evaluationDelaySeconds = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerQuestLog _questLog;
    private QuestSnapshot _lastSnapshot;
    private Coroutine _pendingEvaluationRoutine;
    private bool _hasBaseline;

    private void OnEnable()
    {
        TryBindDependencies();
    }

    private void OnDisable()
    {
        UnbindQuestLog();

        if (_pendingEvaluationRoutine != null)
        {
            StopCoroutine(_pendingEvaluationRoutine);
            _pendingEvaluationRoutine = null;
        }
    }

    private void Update()
    {
        if (_questLog == null || notificationController == null)
        {
            TryBindDependencies();
        }
    }

    private void TryBindDependencies()
    {
        if (notificationController == null)
        {
            notificationController = FindFirstObjectByType<PlayerNotificationController>();
        }

        if (_questLog != null)
        {
            return;
        }

        PlayerQuestLog[] questLogs = FindObjectsByType<PlayerQuestLog>(FindObjectsSortMode.None);

        for (int i = 0; i < questLogs.Length; i++)
        {
            PlayerQuestLog candidate = questLogs[i];

            if (candidate == null || !candidate.IsOwner)
            {
                continue;
            }

            BindQuestLog(candidate);
            break;
        }
    }

    private void BindQuestLog(PlayerQuestLog questLog)
    {
        if (_questLog == questLog)
        {
            return;
        }

        UnbindQuestLog();

        _questLog = questLog;
        _questLog.QuestLogChanged += HandleQuestLogChanged;

        _lastSnapshot = CaptureSnapshot(_questLog);
        _hasBaseline = true;

        _questLog.RequestQuestJournalSnapshot();

        if (verboseLogging)
        {
            Debug.Log("[QuestNotificationBridge] Bound local PlayerQuestLog.", this);
        }
    }

    private void UnbindQuestLog()
    {
        if (_questLog != null)
        {
            _questLog.QuestLogChanged -= HandleQuestLogChanged;
            _questLog = null;
        }

        _hasBaseline = false;
        _lastSnapshot = null;
    }

    private void HandleQuestLogChanged()
    {
        if (_pendingEvaluationRoutine != null)
        {
            StopCoroutine(_pendingEvaluationRoutine);
        }

        _pendingEvaluationRoutine = StartCoroutine(EvaluateAfterDelayRoutine());
    }

    private IEnumerator EvaluateAfterDelayRoutine()
    {
        float delay = Mathf.Max(0f, evaluationDelaySeconds);

        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }
        else
        {
            yield return null;
        }

        _pendingEvaluationRoutine = null;
        EvaluateQuestChanges();
    }

    private void EvaluateQuestChanges()
    {
        if (_questLog == null)
        {
            return;
        }

        QuestSnapshot newSnapshot = CaptureSnapshot(_questLog);

        if (!_hasBaseline || _lastSnapshot == null)
        {
            _lastSnapshot = newSnapshot;
            _hasBaseline = true;
            return;
        }

        EmitDiffNotices(_lastSnapshot, newSnapshot);
        _lastSnapshot = newSnapshot;
    }

    private void EmitDiffNotices(QuestSnapshot oldSnapshot, QuestSnapshot newSnapshot)
    {
        if (notificationController == null || oldSnapshot == null || newSnapshot == null)
        {
            return;
        }

        if (showQuestAccepted)
        {
            foreach (QuestObservedEntry activeQuest in newSnapshot.ActiveQuests.Values)
            {
                if (oldSnapshot.ActiveQuests.ContainsKey(activeQuest.QuestId))
                {
                    continue;
                }

                notificationController.EnqueueProgressNotice(
                    "Quest Accepted",
                    activeQuest.Title,
                    priority: 600);
            }
        }

        foreach (QuestObservedEntry newQuest in newSnapshot.ActiveQuests.Values)
        {
            if (!oldSnapshot.ActiveQuests.TryGetValue(newQuest.QuestId, out QuestObservedEntry oldQuest) ||
                oldQuest == null)
            {
                continue;
            }

            if (showQuestReadyToTurnIn &&
                newQuest.ReadyToTurnIn &&
                !oldQuest.ReadyToTurnIn)
            {
                notificationController.EnqueueProgressNotice(
                    "Ready to Turn In",
                    newQuest.Title,
                    priority: 575);
                continue;
            }

            bool objectiveChanged = oldQuest.Current != newQuest.Current ||
                                    oldQuest.Required != newQuest.Required ||
                                    oldQuest.ObjectiveText != newQuest.ObjectiveText;

            if (showQuestProgress && objectiveChanged && newQuest.Current > oldQuest.Current)
            {
                notificationController.EnqueueProgressNotice(
                    "Quest Updated",
                    FormatObjectiveProgress(newQuest),
                    priority: 550);
            }
        }

        if (showQuestCompleted)
        {
            foreach (string completedQuestId in newSnapshot.CompletedQuestIds)
            {
                if (oldSnapshot.CompletedQuestIds.Contains(completedQuestId))
                {
                    continue;
                }

                string title = completedQuestId;

                if (oldSnapshot.ActiveQuests.TryGetValue(completedQuestId, out QuestObservedEntry oldActiveQuest) &&
                    oldActiveQuest != null &&
                    !string.IsNullOrWhiteSpace(oldActiveQuest.Title))
                {
                    title = oldActiveQuest.Title;
                }

                notificationController.EnqueueProgressNotice(
                    "Quest Complete",
                    title,
                    priority: 700);
            }
        }
    }

    private static QuestSnapshot CaptureSnapshot(PlayerQuestLog questLog)
    {
        QuestSnapshot snapshot = new QuestSnapshot();

        if (questLog == null)
        {
            return snapshot;
        }

        if (questLog.JournalActiveQuests != null)
        {
            for (int i = 0; i < questLog.JournalActiveQuests.Count; i++)
            {
                QuestJournalEntryData entry = questLog.JournalActiveQuests[i];

                if (entry == null || string.IsNullOrWhiteSpace(entry.QuestId))
                {
                    continue;
                }

                string questId = NormalizeId(entry.QuestId);

                snapshot.ActiveQuests[questId] = new QuestObservedEntry(
                    questId,
                    entry.Title,
                    entry.ObjectiveText,
                    entry.Current,
                    entry.Required,
                    entry.ReadyToTurnIn);
            }
        }

        if (questLog.JournalCompletedQuestIds != null)
        {
            for (int i = 0; i < questLog.JournalCompletedQuestIds.Count; i++)
            {
                string questId = NormalizeId(questLog.JournalCompletedQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                snapshot.CompletedQuestIds.Add(questId);
            }
        }

        return snapshot;
    }

    private static string FormatObjectiveProgress(QuestObservedEntry quest)
    {
        if (quest == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(quest.ObjectiveText))
        {
            return $"{quest.Title}: {quest.ObjectiveText} ({quest.Current}/{quest.Required})";
        }

        return $"{quest.Title} ({quest.Current}/{quest.Required})";
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private sealed class QuestSnapshot
    {
        public Dictionary<string, QuestObservedEntry> ActiveQuests { get; } = new();
        public HashSet<string> CompletedQuestIds { get; } = new();
    }

    private sealed class QuestObservedEntry
    {
        public string QuestId { get; }
        public string Title { get; }
        public string ObjectiveText { get; }
        public int Current { get; }
        public int Required { get; }
        public bool ReadyToTurnIn { get; }

        public QuestObservedEntry(
            string questId,
            string title,
            string objectiveText,
            int current,
            int required,
            bool readyToTurnIn)
        {
            QuestId = questId;
            Title = string.IsNullOrWhiteSpace(title) ? questId : title;
            ObjectiveText = objectiveText ?? string.Empty;
            Current = current;
            Required = Mathf.Max(1, required);
            ReadyToTurnIn = readyToTurnIn;
        }
    }
}
