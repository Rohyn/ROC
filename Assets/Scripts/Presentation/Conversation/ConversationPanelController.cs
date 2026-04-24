using UnityEngine;

/// <summary>
/// Persistent controller for NPC conversation UI.
///
/// RESPONSIBILITIES:
/// - bind to the local player's PlayerConversationState
/// - bind to the local PlayerLookController
/// - show/hide the persistent conversation panel
/// - populate topic buttons
/// - switch cursor mode to/from ConversationCursor
///
/// IMPORTANT:
/// - This version fixes an initialization race where the controller might try to bind
///   before the local player exists, then wait too long before retrying.
/// - We now retry EVERY FRAME until both required local references are found.
/// - Once both are bound, polling stops.
/// </summary>
[DisallowMultipleComponent]
public class ConversationPanelController : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private ConversationPanelView panelView;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerConversationState _boundConversationState;
    private PlayerLookController _boundLookController;

    private void Awake()
    {
        if (panelView == null)
        {
            panelView = FindFirstObjectByType<ConversationPanelView>();
        }

        if (panelView != null)
        {
            panelView.Hide();
        }
    }

    private void OnEnable()
    {
        TryBindDependencies();
    }

    private void OnDisable()
    {
        UnbindConversationState();

        if (panelView != null)
        {
            panelView.Hide();
        }
    }

    private void Update()
    {
        // Keep retrying every frame until both dependencies are bound.
        if (_boundConversationState == null || _boundLookController == null)
        {
            TryBindDependencies();
        }
    }

    public void RequestCloseConversation()
    {
        if (_boundConversationState != null)
        {
            _boundConversationState.RequestCloseConversation();
        }
    }

    private void TryBindDependencies()
    {
        if (_boundConversationState == null)
        {
            PlayerConversationState[] states =
                FindObjectsByType<PlayerConversationState>(FindObjectsSortMode.None);

            for (int i = 0; i < states.Length; i++)
            {
                PlayerConversationState state = states[i];
                if (state == null || !state.IsOwner)
                {
                    continue;
                }

                BindConversationState(state);
                break;
            }
        }

        if (_boundLookController == null)
        {
            PlayerLookController[] lookControllers =
                FindObjectsByType<PlayerLookController>(FindObjectsSortMode.None);

            for (int i = 0; i < lookControllers.Length; i++)
            {
                PlayerLookController lookController = lookControllers[i];
                if (lookController == null || !lookController.IsOwner)
                {
                    continue;
                }

                _boundLookController = lookController;

                if (verboseLogging)
                {
                    Debug.Log("[ConversationPanelController] Bound local PlayerLookController.");
                }

                // If a conversation was already open before the look controller was found,
                // refresh now so cursor mode can be corrected immediately.
                RefreshConversationPanel();
                break;
            }
        }
    }

    private void BindConversationState(PlayerConversationState conversationState)
    {
        if (_boundConversationState == conversationState)
        {
            return;
        }

        UnbindConversationState();

        _boundConversationState = conversationState;
        _boundConversationState.ConversationStateChanged += HandleConversationStateChanged;

        if (verboseLogging)
        {
            Debug.Log("[ConversationPanelController] Bound local PlayerConversationState.");
        }

        // IMPORTANT:
        // If the player already has an open conversation when we finally bind,
        // this immediately shows the panel instead of waiting for another interaction.
        RefreshConversationPanel();
    }

    private void UnbindConversationState()
    {
        if (_boundConversationState != null)
        {
            _boundConversationState.ConversationStateChanged -= HandleConversationStateChanged;
            _boundConversationState = null;
        }
    }

    private void HandleConversationStateChanged()
    {
        RefreshConversationPanel();
    }

    private void RefreshConversationPanel()
    {
        if (panelView == null)
        {
            return;
        }

        ClearTopicButtons();

        if (_boundConversationState == null || !_boundConversationState.IsConversationOpen)
        {
            panelView.Hide();

            if (_boundLookController != null &&
                _boundLookController.CurrentCursorMode == PlayerLookController.CursorModeState.ConversationCursor)
            {
                _boundLookController.SetCursorMode(PlayerLookController.CursorModeState.GameplayLocked);
            }

            return;
        }

        panelView.Show();
        panelView.SetSpeakerName(_boundConversationState.SpeakerName);
        panelView.SetResponseText(_boundConversationState.ResponseText);

        if (_boundLookController != null &&
            _boundLookController.CurrentCursorMode != PlayerLookController.CursorModeState.ConversationCursor)
        {
            _boundLookController.SetCursorMode(PlayerLookController.CursorModeState.ConversationCursor);
        }

        if (_boundConversationState.AvailableTopics == null)
        {
            return;
        }

        for (int i = 0; i < _boundConversationState.AvailableTopics.Count; i++)
        {
            PlayerConversationTopicOption topic = _boundConversationState.AvailableTopics[i];
            if (topic == null)
            {
                continue;
            }

            ConversationTopicButtonView buttonView = Instantiate(
                panelView.TopicButtonPrefab,
                panelView.TopicButtonContainer);

            buttonView.Bind(topic.topicId, topic.displayName, HandleTopicClicked);
        }
        // Force a layout rebuild after populating dynamic topic buttons.
        panelView.ForceImmediateLayoutRebuild();
    }

    private void HandleTopicClicked(string topicId)
    {
        if (_boundConversationState == null)
        {
            return;
        }

        _boundConversationState.RequestSelectTopic(topicId);
    }

    private void ClearTopicButtons()
    {
        if (panelView == null || panelView.TopicButtonContainer == null)
        {
            return;
        }

        Transform container = panelView.TopicButtonContainer;

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Destroy(container.GetChild(i).gameObject);
        }
    }
}