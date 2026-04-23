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
/// This controller intentionally keeps conversation separate from the menu system.
/// Conversation uses its own cursor mode so it does not open the inventory menu.
/// </summary>
[DisallowMultipleComponent]
public class ConversationPanelController : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private ConversationPanelView panelView;

    [Header("Binding")]
    [SerializeField] private float searchIntervalSeconds = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerConversationState _boundConversationState;
    private PlayerLookController _boundLookController;
    private float _nextSearchTime;

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
        TryBindDependencies(force: true);
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
        if (Time.time >= _nextSearchTime)
        {
            TryBindDependencies(force: false);
            _nextSearchTime = Time.time + searchIntervalSeconds;
        }
    }

    public void RequestCloseConversation()
    {
        if (_boundConversationState != null)
        {
            _boundConversationState.RequestCloseConversation();
        }
    }

    private void TryBindDependencies(bool force)
    {
        if (force || _boundConversationState == null)
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

        if (force || _boundLookController == null)
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