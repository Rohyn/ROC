using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative quest log for one player.
///
/// RESPONSIBILITIES:
/// - accept quests
/// - track active runtime quest instances
/// - track completed quest history by definition id
/// - consume normalized gameplay events
/// - auto-complete quests when appropriate
/// - emit quest accept/complete gameplay events, including quest tags
/// - publish an owner-only journal snapshot for UI
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerQuestLog : NetworkBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    [SerializeField] private List<QuestInstance> activeQuests = new();
    [SerializeField] private List<string> completedQuestDefinitionIds = new();

    private readonly List<QuestJournalEntryData> _journalActiveQuests = new();
    private readonly List<string> _journalCompletedQuestIds = new();

    public event Action QuestLogChanged;

    public IReadOnlyList<QuestInstance> ActiveQuests => activeQuests;
    public IReadOnlyList<string> CompletedQuestDefinitionIds => completedQuestDefinitionIds;

    public IReadOnlyList<QuestJournalEntryData> JournalActiveQuests => _journalActiveQuests;
    public IReadOnlyList<string> JournalCompletedQuestIds => _journalCompletedQuestIds;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            RequestQuestJournalSnapshot();
        }
    }

    public bool HasActiveQuest(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];

            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            if (instance.Definition.QuestId == questId)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasCompletedQuest(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        for (int i = 0; i < completedQuestDefinitionIds.Count; i++)
        {
            if (completedQuestDefinitionIds[i] == questId)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryAcceptQuest(QuestDefinition definition)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] TryAcceptQuest called on non-server instance.", this);
            return false;
        }

        if (definition == null)
        {
            return false;
        }

        if (!definition.Repeatable)
        {
            if (HasActiveQuest(definition.QuestId) || HasCompletedQuest(definition.QuestId))
            {
                return false;
            }
        }
        else
        {
            if (HasActiveQuest(definition.QuestId))
            {
                return false;
            }
        }

        if (!definition.CanBeAcceptedBy(gameObject))
        {
            return false;
        }

        QuestInstance instance = new QuestInstance(definition);
        instance.RefreshStateDrivenObjectives(gameObject);
        activeQuests.Add(instance);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerQuestLog] Accepted quest '{definition.Title}' ({definition.QuestId}).", this);
        }

        RecordGameplayEventInternal(GameplayEventData.CreateQuestAcceptedEvent(definition, instance.InstanceId));

        ProcessCompletedStates();
        RaiseQuestLogChanged();

        return true;
    }

    public bool TryTurnInQuest(string questId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] TryTurnInQuest called on non-server instance.", this);
            return false;
        }

        QuestInstance instance = FindActiveQuestInstance(questId);

        if (instance == null || !instance.CanTurnIn())
        {
            return false;
        }

        CompleteQuestInternal(instance, emitQuestCompletedEvent: true, emitQuestTurnedInEvent: true);
        RaiseQuestLogChanged();

        return true;
    }

    public bool TryAbandonQuest(string questId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] TryAbandonQuest called on non-server instance.", this);
            return false;
        }

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];

            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            if (instance.Definition.QuestId != questId)
            {
                continue;
            }

            instance.MarkAbandoned();
            activeQuests.RemoveAt(i);

            if (verboseLogging)
            {
                Debug.Log($"[PlayerQuestLog] Abandoned quest '{questId}'.", this);
            }

            RaiseQuestLogChanged();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Server-only entry point for all gameplay event ingestion.
    /// </summary>
    public void RecordGameplayEvent(GameplayEventData eventData)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] RecordGameplayEvent called on non-server instance.", this);
            return;
        }

        RecordGameplayEventInternal(eventData);
        ProcessCompletedStates();
        RaiseQuestLogChanged();
    }

    /// <summary>
    /// Owner/client call used by UI to ask the server for the latest journal snapshot.
    /// </summary>
    public void RequestQuestJournalSnapshot()
    {
        if (!IsOwner)
        {
            return;
        }

        if (IsServer)
        {
            SendQuestJournalSnapshotToOwner();
        }
        else
        {
            RequestQuestJournalSnapshotRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestQuestJournalSnapshotRpc()
    {
        SendQuestJournalSnapshotToOwner();
    }

    private void RecordGameplayEventInternal(GameplayEventData eventData)
    {
        if (eventData == null)
        {
            return;
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerQuestLog] Processing gameplay event '{eventData.EventType}'.", this);
        }

        bool anyChanged = false;

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];

            if (instance == null)
            {
                continue;
            }

            bool changed = instance.ApplyGameplayEvent(eventData, gameObject);

            if (changed)
            {
                anyChanged = true;
            }
        }

        if (anyChanged && verboseLogging)
        {
            Debug.Log("[PlayerQuestLog] One or more active quests changed progress.", this);
        }
    }

    private void ProcessCompletedStates()
    {
        for (int i = activeQuests.Count - 1; i >= 0; i--)
        {
            QuestInstance instance = activeQuests[i];

            if (instance == null)
            {
                activeQuests.RemoveAt(i);
                continue;
            }

            if (instance.State == QuestInstance.QuestInstanceState.Completed)
            {
                CompleteQuestInternal(instance, emitQuestCompletedEvent: true, emitQuestTurnedInEvent: false);
            }
        }
    }

    private void CompleteQuestInternal(
        QuestInstance instance,
        bool emitQuestCompletedEvent,
        bool emitQuestTurnedInEvent)
    {
        if (instance == null || instance.Definition == null)
        {
            return;
        }

        string questId = instance.Definition.QuestId;
        string questTitle = instance.Definition.Title;

        instance.MarkCompleted();
        activeQuests.Remove(instance);

        if (!completedQuestDefinitionIds.Contains(questId))
        {
            completedQuestDefinitionIds.Add(questId);
        }

        if (instance.Definition.Rewards != null)
        {
            instance.Definition.Rewards.ApplyTo(gameObject);
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerQuestLog] Completed quest '{questTitle}' ({questId}).", this);
        }

        if (emitQuestCompletedEvent)
        {
            RecordGameplayEventInternal(GameplayEventData.CreateQuestCompletedEvent(instance.Definition, instance.InstanceId));
        }

        if (emitQuestTurnedInEvent)
        {
            RecordGameplayEventInternal(GameplayEventData.CreateQuestTurnedInEvent(instance.Definition, instance.InstanceId));
        }
    }

    private QuestInstance FindActiveQuestInstance(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            return null;
        }

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];

            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            if (instance.Definition.QuestId == questId)
            {
                return instance;
            }
        }

        return null;
    }

    private void RaiseQuestLogChanged()
    {
        if (IsServer)
        {
            SendQuestJournalSnapshotToOwner();
        }

        QuestLogChanged?.Invoke();
    }

    private void SendQuestJournalSnapshotToOwner()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        QuestJournalEntryNet[] activeEntries = BuildActiveQuestSnapshot();
        FixedString64Bytes[] completedIds = BuildCompletedQuestSnapshot();

        ReceiveQuestJournalSnapshotRpc(activeEntries, completedIds);
    }

    private QuestJournalEntryNet[] BuildActiveQuestSnapshot()
    {
        List<QuestJournalEntryNet> result = new List<QuestJournalEntryNet>();

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];

            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            string objectiveText = string.Empty;
            int current = 0;
            int required = 1;

            TryGetPrimaryObjectiveDisplay(instance, out objectiveText, out current, out required);

            result.Add(new QuestJournalEntryNet(
                instance.Definition.QuestId,
                instance.Definition.Title,
                instance.Definition.Description,
                objectiveText,
                current,
                required,
                instance.State == QuestInstance.QuestInstanceState.ReadyToTurnIn));
        }

        return result.ToArray();
    }

    private FixedString64Bytes[] BuildCompletedQuestSnapshot()
    {
        List<FixedString64Bytes> result = new List<FixedString64Bytes>();

        for (int i = 0; i < completedQuestDefinitionIds.Count; i++)
        {
            string questId = completedQuestDefinitionIds[i];

            if (string.IsNullOrWhiteSpace(questId))
            {
                continue;
            }

            result.Add(new FixedString64Bytes(Truncate(questId, 63)));
        }

        return result.ToArray();
    }

    private static bool TryGetPrimaryObjectiveDisplay(
        QuestInstance instance,
        out string objectiveText,
        out int current,
        out int required)
    {
        objectiveText = string.Empty;
        current = 0;
        required = 1;

        if (instance == null || instance.Definition == null)
        {
            return false;
        }

        QuestObjectiveDefinition[] objectives = instance.Definition.Objectives;
        QuestInstance.QuestObjectiveProgressData[] progress = instance.ObjectiveProgress;

        if (objectives == null || progress == null)
        {
            return false;
        }

        int fallbackIndex = -1;

        for (int i = 0; i < objectives.Length && i < progress.Length; i++)
        {
            if (objectives[i] == null || progress[i] == null)
            {
                continue;
            }

            if (fallbackIndex < 0)
            {
                fallbackIndex = i;
            }

            if (!progress[i].IsComplete)
            {
                objectiveText = objectives[i].Description;
                current = progress[i].CurrentCount;
                required = objectives[i].RequiredQuantity;
                return true;
            }
        }

        if (fallbackIndex >= 0)
        {
            objectiveText = objectives[fallbackIndex].Description;
            current = progress[fallbackIndex].CurrentCount;
            required = objectives[fallbackIndex].RequiredQuantity;
            return true;
        }

        return false;
    }

    [Rpc(SendTo.Owner)]
    private void ReceiveQuestJournalSnapshotRpc(
        QuestJournalEntryNet[] activeEntries,
        FixedString64Bytes[] completedIds)
    {
        _journalActiveQuests.Clear();
        _journalCompletedQuestIds.Clear();

        if (activeEntries != null)
        {
            for (int i = 0; i < activeEntries.Length; i++)
            {
                QuestJournalEntryNet entry = activeEntries[i];

                _journalActiveQuests.Add(new QuestJournalEntryData(
                    entry.QuestId.ToString(),
                    entry.Title.ToString(),
                    entry.Description.ToString(),
                    entry.ObjectiveText.ToString(),
                    entry.Current,
                    entry.Required,
                    entry.ReadyToTurnIn));
            }
        }

        if (completedIds != null)
        {
            for (int i = 0; i < completedIds.Length; i++)
            {
                string questId = completedIds[i].ToString();

                if (!string.IsNullOrWhiteSpace(questId))
                {
                    _journalCompletedQuestIds.Add(questId);
                }
            }
        }

        QuestLogChanged?.Invoke();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}

public class QuestJournalEntryData
{
    public string QuestId { get; }
    public string Title { get; }
    public string Description { get; }
    public string ObjectiveText { get; }
    public int Current { get; }
    public int Required { get; }
    public bool ReadyToTurnIn { get; }

    public QuestJournalEntryData(
        string questId,
        string title,
        string description,
        string objectiveText,
        int current,
        int required,
        bool readyToTurnIn)
    {
        QuestId = questId;
        Title = title;
        Description = description;
        ObjectiveText = objectiveText;
        Current = current;
        Required = Mathf.Max(1, required);
        ReadyToTurnIn = readyToTurnIn;
    }
}

public struct QuestJournalEntryNet : INetworkSerializable
{
    public FixedString64Bytes QuestId;
    public FixedString128Bytes Title;
    public FixedString512Bytes Description;
    public FixedString512Bytes ObjectiveText;
    public int Current;
    public int Required;
    public bool ReadyToTurnIn;

    public QuestJournalEntryNet(
        string questId,
        string title,
        string description,
        string objectiveText,
        int current,
        int required,
        bool readyToTurnIn)
    {
        QuestId = new FixedString64Bytes(Truncate(questId, 63));
        Title = new FixedString128Bytes(Truncate(title, 127));
        Description = new FixedString512Bytes(Truncate(description, 511));
        ObjectiveText = new FixedString512Bytes(Truncate(objectiveText, 511));
        Current = current;
        Required = Mathf.Max(1, required);
        ReadyToTurnIn = readyToTurnIn;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref QuestId);
        serializer.SerializeValue(ref Title);
        serializer.SerializeValue(ref Description);
        serializer.SerializeValue(ref ObjectiveText);
        serializer.SerializeValue(ref Current);
        serializer.SerializeValue(ref Required);
        serializer.SerializeValue(ref ReadyToTurnIn);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}