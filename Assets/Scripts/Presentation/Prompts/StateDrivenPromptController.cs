using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owner-local observer for state-driven prompts.
/// Useful for tutorial-style guidance derived from already-replicated player state.
/// It never mutates gameplay state.
/// </summary>
[DisallowMultipleComponent]
public class StateDrivenPromptController : MonoBehaviour
{
    [Header("Prompt Rules")]
    [SerializeField] private PromptDefinition[] stateDrivenPrompts;

    [Header("Timing")]
    [SerializeField] private float fallbackEvaluationInterval = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerPromptController _promptController;
    private PlayerProgressState _progressState;
    private ROC.Inventory.PlayerInventory _inventory;
    private PlayerQuestLog _questLog;
    private PlayerConversationState _conversationState;

    private float _nextFallbackEvaluationTime;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;

        TryBindDependencies();
        EvaluatePrompts();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;

        UnbindProgressState();
        UnbindInventory();
        UnbindQuestLog();
        UnbindConversationState();

        _promptController = null;
    }

    private void Update()
    {
        if (!HasCoreBindings())
        {
            TryBindDependencies();
        }

        if (Time.unscaledTime >= _nextFallbackEvaluationTime)
        {
            _nextFallbackEvaluationTime = Time.unscaledTime + Mathf.Max(0.05f, fallbackEvaluationInterval);
            EvaluatePrompts();
        }
    }

    private void TryBindDependencies()
    {
        if (_promptController == null)
        {
            PlayerPromptController[] controllers = FindObjectsByType<PlayerPromptController>(FindObjectsSortMode.None);

            for (int i = 0; i < controllers.Length; i++)
            {
                PlayerPromptController controller = controllers[i];

                if (controller == null || !controller.IsOwner)
                {
                    continue;
                }

                _promptController = controller;

                if (verboseLogging)
                {
                    Debug.Log("[StateDrivenPromptController] Bound local PlayerPromptController.", this);
                }

                break;
            }
        }

        if (_progressState == null)
        {
            PlayerProgressState[] states = FindObjectsByType<PlayerProgressState>(FindObjectsSortMode.None);

            for (int i = 0; i < states.Length; i++)
            {
                PlayerProgressState state = states[i];

                if (state == null || !state.IsOwner)
                {
                    continue;
                }

                BindProgressState(state);
                break;
            }
        }

        if (_inventory == null)
        {
            ROC.Inventory.PlayerInventory[] inventories =
                FindObjectsByType<ROC.Inventory.PlayerInventory>(FindObjectsSortMode.None);

            for (int i = 0; i < inventories.Length; i++)
            {
                ROC.Inventory.PlayerInventory inventory = inventories[i];

                if (inventory == null || !inventory.IsOwner)
                {
                    continue;
                }

                BindInventory(inventory);
                break;
            }
        }

        if (_questLog == null)
        {
            PlayerQuestLog[] questLogs = FindObjectsByType<PlayerQuestLog>(FindObjectsSortMode.None);

            for (int i = 0; i < questLogs.Length; i++)
            {
                PlayerQuestLog questLog = questLogs[i];

                if (questLog == null || !questLog.IsOwner)
                {
                    continue;
                }

                BindQuestLog(questLog);
                break;
            }
        }

        if (_conversationState == null)
        {
            PlayerConversationState[] conversationStates =
                FindObjectsByType<PlayerConversationState>(FindObjectsSortMode.None);

            for (int i = 0; i < conversationStates.Length; i++)
            {
                PlayerConversationState conversationState = conversationStates[i];

                if (conversationState == null || !conversationState.IsOwner)
                {
                    continue;
                }

                BindConversationState(conversationState);
                break;
            }
        }
    }

    private bool HasCoreBindings()
    {
        return _promptController != null && _progressState != null;
    }

    private void BindProgressState(PlayerProgressState progressState)
    {
        if (_progressState == progressState)
        {
            return;
        }

        UnbindProgressState();

        _progressState = progressState;
        _progressState.FlagsChanged += HandleObservedStateChanged;

        if (verboseLogging)
        {
            Debug.Log("[StateDrivenPromptController] Bound local PlayerProgressState.", this);
        }
    }

    private void BindInventory(ROC.Inventory.PlayerInventory inventory)
    {
        if (_inventory == inventory)
        {
            return;
        }

        UnbindInventory();

        _inventory = inventory;
        _inventory.InventoryChanged += HandleObservedStateChanged;

        if (verboseLogging)
        {
            Debug.Log("[StateDrivenPromptController] Bound local PlayerInventory.", this);
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
        _questLog.QuestLogChanged += HandleObservedStateChanged;
        _questLog.RequestQuestJournalSnapshot();

        if (verboseLogging)
        {
            Debug.Log("[StateDrivenPromptController] Bound local PlayerQuestLog.", this);
        }
    }

    private void BindConversationState(PlayerConversationState conversationState)
    {
        if (_conversationState == conversationState)
        {
            return;
        }

        UnbindConversationState();

        _conversationState = conversationState;
        _conversationState.ConversationStateChanged += HandleObservedStateChanged;

        if (verboseLogging)
        {
            Debug.Log("[StateDrivenPromptController] Bound local PlayerConversationState.", this);
        }
    }

    private void UnbindProgressState()
    {
        if (_progressState != null)
        {
            _progressState.FlagsChanged -= HandleObservedStateChanged;
            _progressState = null;
        }
    }

    private void UnbindInventory()
    {
        if (_inventory != null)
        {
            _inventory.InventoryChanged -= HandleObservedStateChanged;
            _inventory = null;
        }
    }

    private void UnbindQuestLog()
    {
        if (_questLog != null)
        {
            _questLog.QuestLogChanged -= HandleObservedStateChanged;
            _questLog = null;
        }
    }

    private void UnbindConversationState()
    {
        if (_conversationState != null)
        {
            _conversationState.ConversationStateChanged -= HandleObservedStateChanged;
            _conversationState = null;
        }
    }

    private void HandleObservedStateChanged()
    {
        EvaluatePrompts();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EvaluatePrompts();
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        EvaluatePrompts();
    }

    private void EvaluatePrompts()
    {
        if (!HasCoreBindings() || stateDrivenPrompts == null || stateDrivenPrompts.Length == 0)
        {
            return;
        }

        PromptDefinition bestPrompt = null;
        int bestPriority = int.MinValue;

        for (int i = 0; i < stateDrivenPrompts.Length; i++)
        {
            PromptDefinition prompt = stateDrivenPrompts[i];

            if (prompt == null)
            {
                continue;
            }

            if (!prompt.IsEligibleByState(_progressState, _inventory, _questLog, _conversationState))
            {
                continue;
            }

            if (!_promptController.CanDisplayPromptLocal(prompt, respectDefinitionConditions: false))
            {
                continue;
            }

            if (bestPrompt == null || prompt.Priority > bestPriority)
            {
                bestPrompt = prompt;
                bestPriority = prompt.Priority;
            }
        }

        if (bestPrompt == null)
        {
            return;
        }

        _promptController.TryShowPromptLocal(
            bestPrompt,
            respectDefinitionConditions: false,
            force: false);
    }
}
