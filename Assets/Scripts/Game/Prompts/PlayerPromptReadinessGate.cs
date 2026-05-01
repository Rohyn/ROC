using ROC.Inventory;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-local startup gate for state-driven prompts.
///
/// Prevents prompts from evaluating during the short window where the player has spawned
/// but saved/restored owner state has not fully settled yet.
///
/// This avoids cases where returning characters briefly look like they are in Area_Intro
/// and receive the first Aidan tutorial prompt before persistence restore finishes.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerPromptReadinessGate : NetworkBehaviour
{
    [Header("Startup")]
    [Tooltip("Minimum time after owner spawn before state-driven prompts may evaluate.")]
    [SerializeField] private float minimumStartupDelaySeconds = 1.25f;

    [Tooltip("State must remain unchanged for this long before prompts may evaluate.")]
    [SerializeField] private float requiredQuietSeconds = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerAreaStreamingController _areaStreaming;
    private PlayerProgressState _progressState;
    private PlayerInventory _inventory;
    private PlayerQuestLog _questLog;

    private float _notBeforeRealtime;
    private float _lastStateChangeRealtime;

    private string _lastAreaSceneName;
    private bool _lastTransferInProgress;
    private bool _loggedReady;

    public bool IsReadyForPrompts
    {
        get
        {
            if (!IsSpawned || !IsOwner)
            {
                return false;
            }

            if (Time.realtimeSinceStartup < _notBeforeRealtime)
            {
                return false;
            }

            if (Time.realtimeSinceStartup - _lastStateChangeRealtime < requiredQuietSeconds)
            {
                return false;
            }

            if (_areaStreaming != null)
            {
                if (!_areaStreaming.HasInitializedAreaState)
                {
                    return false;
                }

                if (_areaStreaming.IsAreaTransferInProgress)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(_areaStreaming.CurrentAreaSceneName))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public override void OnNetworkSpawn()
    {
        BindDependencies();
        ResetGate("network spawn");
    }

    public override void OnNetworkDespawn()
    {
        UnbindDependencies();
    }

    private void OnEnable()
    {
        BindDependencies();
        ResetGate("enable");
    }

    private void OnDisable()
    {
        UnbindDependencies();
    }

    private void Update()
    {
        BindDependencies();

        if (_areaStreaming != null)
        {
            string currentArea = _areaStreaming.CurrentAreaSceneName ?? string.Empty;
            bool transferInProgress = _areaStreaming.IsAreaTransferInProgress;

            if (currentArea != _lastAreaSceneName ||
                transferInProgress != _lastTransferInProgress)
            {
                _lastAreaSceneName = currentArea;
                _lastTransferInProgress = transferInProgress;
                MarkStateChanged("area/transfer state changed");
            }
        }

        if (verboseLogging && !_loggedReady && IsReadyForPrompts)
        {
            _loggedReady = true;

            Debug.Log(
                $"[PlayerPromptReadinessGate] Prompts ready. CurrentArea='{_lastAreaSceneName}'.",
                this);
        }
    }

    private void BindDependencies()
    {
        if (_areaStreaming == null)
        {
            _areaStreaming = GetComponent<PlayerAreaStreamingController>();
        }

        if (_progressState == null)
        {
            _progressState = GetComponent<PlayerProgressState>();

            if (_progressState != null)
            {
                _progressState.FlagsChanged -= HandleProgressFlagsChanged;
                _progressState.FlagsChanged += HandleProgressFlagsChanged;
            }
        }

        if (_inventory == null)
        {
            _inventory = GetComponent<PlayerInventory>();

            if (_inventory != null)
            {
                _inventory.InventoryChanged -= HandleInventoryChanged;
                _inventory.InventoryChanged += HandleInventoryChanged;
            }
        }

        if (_questLog == null)
        {
            _questLog = GetComponent<PlayerQuestLog>();

            if (_questLog != null)
            {
                _questLog.QuestLogChanged -= HandleQuestLogChanged;
                _questLog.QuestLogChanged += HandleQuestLogChanged;
            }
        }
    }

    private void UnbindDependencies()
    {
        if (_progressState != null)
        {
            _progressState.FlagsChanged -= HandleProgressFlagsChanged;
        }

        if (_inventory != null)
        {
            _inventory.InventoryChanged -= HandleInventoryChanged;
        }

        if (_questLog != null)
        {
            _questLog.QuestLogChanged -= HandleQuestLogChanged;
        }

        _areaStreaming = null;
        _progressState = null;
        _inventory = null;
        _questLog = null;
    }

    private void ResetGate(string reason)
    {
        float now = Time.realtimeSinceStartup;

        _notBeforeRealtime = now + Mathf.Max(0f, minimumStartupDelaySeconds);
        _lastStateChangeRealtime = now;
        _loggedReady = false;

        if (_areaStreaming != null)
        {
            _lastAreaSceneName = _areaStreaming.CurrentAreaSceneName ?? string.Empty;
            _lastTransferInProgress = _areaStreaming.IsAreaTransferInProgress;
        }
        else
        {
            _lastAreaSceneName = string.Empty;
            _lastTransferInProgress = false;
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerPromptReadinessGate] Reset gate: {reason}.", this);
        }
    }

    private void MarkStateChanged(string reason)
    {
        _lastStateChangeRealtime = Time.realtimeSinceStartup;
        _loggedReady = false;

        if (verboseLogging)
        {
            Debug.Log($"[PlayerPromptReadinessGate] State changed: {reason}.", this);
        }
    }

    private void HandleProgressFlagsChanged()
    {
        MarkStateChanged("progress flags changed");
    }

    private void HandleInventoryChanged()
    {
        MarkStateChanged("inventory changed");
    }

    private void HandleQuestLogChanged()
    {
        MarkStateChanged("quest log changed");
    }
}