using UnityEngine;

/// <summary>
/// Starts a conversation between the interacting player and the NPC on this object.
///
/// Attach this to the same GameObject as:
/// - GenericInteractable
/// - NPCConversationComponent
///
/// This action assumes GenericInteractable.TryInteract is already executing on the server.
/// </summary>
[RequireComponent(typeof(NPCConversationComponent))]
public class StartConversationAction : InteractableAction
{
    [Header("Flow Control")]
    [Tooltip("If true, stop later actions after starting the conversation.")]
    [SerializeField] private bool stopFurtherActions = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private NPCConversationComponent _conversationComponent;

    private void Awake()
    {
        _conversationComponent = GetComponent<NPCConversationComponent>();
    }

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (_conversationComponent == null)
        {
            return false;
        }

        PlayerConversationState playerConversation = context.InteractorObject.GetComponent<PlayerConversationState>();
        return playerConversation != null;
    }

    public override void Execute(InteractionContext context)
    {
        if (_conversationComponent == null || context == null || context.InteractorObject == null)
        {
            return;
        }

        PlayerConversationState playerConversation = context.InteractorObject.GetComponent<PlayerConversationState>();
        if (playerConversation == null)
        {
            return;
        }

        playerConversation.StartConversation(_conversationComponent);

        if (verboseLogging)
        {
            Debug.Log($"[StartConversationAction] Started conversation with '{_conversationComponent.DisplayName}'.", this);
        }

        if (stopFurtherActions)
        {
            context.StopFurtherActions = true;
        }
    }
}