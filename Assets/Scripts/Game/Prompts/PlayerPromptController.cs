using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-player owner-local prompt queue and server-to-owner prompt receiver.
/// Attach this to the gameplay player prefab.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerPromptController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private PromptCatalog promptCatalog;
    [SerializeField] private PromptToastView toastView;

    [Header("Queue Rules")]
    [SerializeField] private float secondsBetweenPrompts = 0.35f;
    [SerializeField] private bool allowHighPriorityInterrupts = true;
    [SerializeField] private int interruptPriorityDelta = 500;

    [Header("Server-Pushed Prompt Rules")]
    [Tooltip("Usually false. Server-pushed prompts are assumed to come from authoritative gameplay.")]
    [SerializeField] private bool respectDefinitionConditionsOnServerPush = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private readonly List<QueuedPrompt> _queue = new();
    private readonly HashSet<string> _shownThisSession = new();
    private readonly Dictionary<string, float> _cooldownUntilByPromptId = new();

    private Coroutine _displayRoutine;
    private PromptDefinition _currentlyDisplayedPrompt;

    private PlayerProgressState _progressState;
    private ROC.Inventory.PlayerInventory _inventory;
    private PlayerQuestLog _questLog;
    private PlayerConversationState _conversationState;

    public PromptCatalog PromptCatalog => promptCatalog;

    private void Awake()
    {
        CachePlayerStateReferences();
    }

    public override void OnNetworkSpawn()
    {
        CachePlayerStateReferences();

        if (IsOwner && toastView == null)
        {
            toastView = FindFirstObjectByType<PromptToastView>();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_displayRoutine != null)
        {
            StopCoroutine(_displayRoutine);
            _displayRoutine = null;
        }

        _queue.Clear();
        _currentlyDisplayedPrompt = null;

        if (IsOwner && toastView != null)
        {
            toastView.Hide();
        }
    }

    public void SendPromptToOwnerServer(string promptId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerPromptController] SendPromptToOwnerServer called on a non-server instance.", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(promptId))
        {
            return;
        }

        ReceivePromptRequestRpc(promptId.Trim());
    }

    public bool TryShowPromptLocal(PromptDefinition prompt, bool respectDefinitionConditions = false, bool force = false)
    {
        if (!IsOwner || prompt == null)
        {
            return false;
        }

        CachePlayerStateReferences();

        if (!force && !CanDisplayPromptLocal(prompt, respectDefinitionConditions))
        {
            return false;
        }

        if (IsPromptAlreadyQueued(prompt.PromptId))
        {
            return false;
        }

        if (_currentlyDisplayedPrompt != null && IdsMatch(_currentlyDisplayedPrompt.PromptId, prompt.PromptId))
        {
            return false;
        }

        if (allowHighPriorityInterrupts &&
            _currentlyDisplayedPrompt != null &&
            prompt.Priority >= _currentlyDisplayedPrompt.Priority + interruptPriorityDelta)
        {
            InterruptCurrentPrompt();
        }

        _queue.Add(new QueuedPrompt(prompt, Time.realtimeSinceStartup));
        _queue.Sort(CompareQueuedPrompts);

        if (_displayRoutine == null)
        {
            _displayRoutine = StartCoroutine(DisplayRoutine());
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerPromptController] Queued prompt '{prompt.PromptId}'.", this);
        }

        return true;
    }

    public bool TryShowPromptByIdLocal(string promptId, bool respectDefinitionConditions = false, bool force = false)
    {
        if (!TryResolvePrompt(promptId, out PromptDefinition prompt) || prompt == null)
        {
            if (verboseLogging)
            {
                Debug.LogWarning($"[PlayerPromptController] Unknown prompt id '{promptId}'.", this);
            }

            return false;
        }

        return TryShowPromptLocal(prompt, respectDefinitionConditions, force);
    }

    public bool CanDisplayPromptLocal(PromptDefinition prompt, bool respectDefinitionConditions = false)
    {
        if (!IsOwner || prompt == null)
        {
            return false;
        }

        string promptId = NormalizeId(prompt.PromptId);

        if (string.IsNullOrWhiteSpace(promptId) || string.IsNullOrWhiteSpace(prompt.MessageText))
        {
            return false;
        }

        if (prompt.RepeatMode == PromptRepeatMode.OncePerSession && _shownThisSession.Contains(promptId))
        {
            return false;
        }

        if (prompt.CooldownSeconds > 0f &&
            _cooldownUntilByPromptId.TryGetValue(promptId, out float cooldownUntil) &&
            Time.realtimeSinceStartup < cooldownUntil)
        {
            return false;
        }

        if (respectDefinitionConditions &&
            prompt.Conditions != null &&
            !prompt.Conditions.IsSatisfiedBy(_progressState, _inventory, _questLog, _conversationState))
        {
            return false;
        }

        return true;
    }

    [Rpc(SendTo.Owner)]
    private void ReceivePromptRequestRpc(string promptId)
    {
        TryShowPromptByIdLocal(
            promptId,
            respectDefinitionConditionsOnServerPush,
            force: false);
    }

    private IEnumerator DisplayRoutine()
    {
        while (_queue.Count > 0)
        {
            QueuedPrompt queuedPrompt = _queue[0];
            _queue.RemoveAt(0);

            PromptDefinition prompt = queuedPrompt.Prompt;

            if (prompt == null || !CanDisplayPromptLocal(prompt, respectDefinitionConditions: false))
            {
                continue;
            }

            _currentlyDisplayedPrompt = prompt;
            MarkPromptDisplayed(prompt);

            if (toastView == null)
            {
                toastView = FindFirstObjectByType<PromptToastView>();
            }

            if (toastView != null)
            {
                toastView.Show(prompt.SpeakerName, prompt.MessageText);
            }
            else
            {
                Debug.LogWarning("[PlayerPromptController] Cannot show prompt because no PromptToastView was found.", this);
            }

            yield return new WaitForSeconds(prompt.DisplaySeconds);

            if (toastView != null)
            {
                toastView.Hide();
            }

            _currentlyDisplayedPrompt = null;

            if (secondsBetweenPrompts > 0f)
            {
                yield return new WaitForSeconds(secondsBetweenPrompts);
            }
        }

        _displayRoutine = null;
    }

    private void InterruptCurrentPrompt()
    {
        if (_displayRoutine != null)
        {
            StopCoroutine(_displayRoutine);
            _displayRoutine = null;
        }

        _currentlyDisplayedPrompt = null;

        if (toastView != null)
        {
            toastView.Hide();
        }
    }

    private void MarkPromptDisplayed(PromptDefinition prompt)
    {
        if (prompt == null)
        {
            return;
        }

        string promptId = NormalizeId(prompt.PromptId);

        if (string.IsNullOrWhiteSpace(promptId))
        {
            return;
        }

        if (prompt.RepeatMode == PromptRepeatMode.OncePerSession)
        {
            _shownThisSession.Add(promptId);
        }

        if (prompt.CooldownSeconds > 0f)
        {
            _cooldownUntilByPromptId[promptId] = Time.realtimeSinceStartup + prompt.CooldownSeconds;
        }
    }

    private bool TryResolvePrompt(string promptId, out PromptDefinition prompt)
    {
        prompt = null;

        if (string.IsNullOrWhiteSpace(promptId) || promptCatalog == null)
        {
            return false;
        }

        return promptCatalog.TryGetDefinition(promptId, out prompt);
    }

    private void CachePlayerStateReferences()
    {
        if (_progressState == null)
        {
            _progressState = GetComponent<PlayerProgressState>();
        }

        if (_inventory == null)
        {
            _inventory = GetComponent<ROC.Inventory.PlayerInventory>();
        }

        if (_questLog == null)
        {
            _questLog = GetComponent<PlayerQuestLog>();
        }

        if (_conversationState == null)
        {
            _conversationState = GetComponent<PlayerConversationState>();
        }
    }

    private bool IsPromptAlreadyQueued(string promptId)
    {
        string normalizedPromptId = NormalizeId(promptId);

        for (int i = 0; i < _queue.Count; i++)
        {
            QueuedPrompt queuedPrompt = _queue[i];

            if (queuedPrompt.Prompt == null)
            {
                continue;
            }

            if (IdsMatch(queuedPrompt.Prompt.PromptId, normalizedPromptId))
            {
                return true;
            }
        }

        return false;
    }

    private static int CompareQueuedPrompts(QueuedPrompt a, QueuedPrompt b)
    {
        if (a.Prompt == null && b.Prompt == null) return 0;
        if (a.Prompt == null) return 1;
        if (b.Prompt == null) return -1;

        int priorityCompare = b.Prompt.Priority.CompareTo(a.Prompt.Priority);

        if (priorityCompare != 0)
        {
            return priorityCompare;
        }

        return a.QueuedTime.CompareTo(b.QueuedTime);
    }

    private static bool IdsMatch(string a, string b)
    {
        return string.Equals(NormalizeId(a), NormalizeId(b), System.StringComparison.Ordinal);
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private sealed class QueuedPrompt
    {
        public PromptDefinition Prompt { get; }
        public float QueuedTime { get; }

        public QueuedPrompt(PromptDefinition prompt, float queuedTime)
        {
            Prompt = prompt;
            QueuedTime = queuedTime;
        }
    }
}
