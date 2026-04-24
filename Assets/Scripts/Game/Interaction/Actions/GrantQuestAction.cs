using UnityEngine;

/// <summary>
/// Grants a quest to the interacting player.
///
/// This can be attached to:
/// - NPC interactables
/// - objects like notice boards
/// - future quest terminal / contract board style objects
///
/// IMPORTANT:
/// This action only ACCEPTS the quest. It does not turn it in.
/// For NPCs, this can be paired with StartConversationAction, either:
/// - before it in execution order, or
/// - after it if StartConversationAction does not stop further actions
/// </summary>
public class GrantQuestAction : InteractableAction
{
    [Header("Quest Grant")]
    [SerializeField] private QuestDefinition questToGrant;

    [Header("Flow Control")]
    [Tooltip("If true, stop later actions when the quest is successfully granted.")]
    [SerializeField] private bool stopFurtherActionsOnSuccess = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (questToGrant == null)
        {
            return false;
        }

        PlayerQuestLog questLog = context.InteractorObject.GetComponent<PlayerQuestLog>();
        if (questLog == null)
        {
            return false;
        }

        // Let PlayerQuestLog perform the real authoritative acceptance rules.
        return true;
    }

    public override void Execute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null || questToGrant == null)
        {
            return;
        }

        PlayerQuestLog questLog = context.InteractorObject.GetComponent<PlayerQuestLog>();
        if (questLog == null)
        {
            return;
        }

        bool accepted = questLog.TryAcceptQuest(questToGrant);

        if (verboseLogging)
        {
            Debug.Log(accepted
                ? $"[GrantQuestAction] Granted quest '{questToGrant.Title}' ({questToGrant.QuestId})."
                : $"[GrantQuestAction] Quest '{questToGrant.Title}' ({questToGrant.QuestId}) was not granted.");
        }

        if (accepted && stopFurtherActionsOnSuccess)
        {
            context.StopFurtherActions = true;
        }
    }
}