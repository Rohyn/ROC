using UnityEngine;

/// <summary>
/// Interaction action that sends a prompt to the interacting player's owning client.
/// Use this for interactables, blockers, workstations, scenario objects, etc.
/// </summary>
public class SendPromptAction : InteractableAction
{
    [Header("Prompt")]
    [SerializeField] private PromptDefinition promptDefinition;

    [Tooltip("Optional explicit ID. If blank, PromptDefinition.PromptId is used.")]
    [SerializeField] private string promptId;

    [Header("Flow Control")]
    [SerializeField] private bool stopFurtherActionsAfterSend = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ResolvePromptId()))
        {
            return false;
        }

        return context.InteractorObject.GetComponent<PlayerPromptController>() != null;
    }

    public override void Execute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return;
        }

        PlayerPromptController promptController = context.InteractorObject.GetComponent<PlayerPromptController>();

        if (promptController == null)
        {
            Debug.LogWarning("[SendPromptAction] Interactor has no PlayerPromptController.", this);
            return;
        }

        string resolvedPromptId = ResolvePromptId();

        if (string.IsNullOrWhiteSpace(resolvedPromptId))
        {
            return;
        }

        promptController.SendPromptToOwnerServer(resolvedPromptId);

        if (verboseLogging)
        {
            Debug.Log($"[SendPromptAction] Sent prompt '{resolvedPromptId}' to interactor.", this);
        }

        if (stopFurtherActionsAfterSend)
        {
            context.StopFurtherActions = true;
        }
    }

    private string ResolvePromptId()
    {
        if (!string.IsNullOrWhiteSpace(promptId))
        {
            return promptId.Trim();
        }

        return promptDefinition != null ? promptDefinition.PromptId : string.Empty;
    }
}
