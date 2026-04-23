using UnityEngine;
using ROC.Statuses;

/// <summary>
/// Generic interaction action that toggles a single status on the interactor.
///
/// Behavior:
/// - if the status is absent, apply it
/// - if the status is present, remove it
///
/// This is useful for:
/// - resting on/off
/// - toggled stance-like statuses later
/// - cursed shrine effects
/// - simple seat / stand interactions
///
/// IMPORTANT:
/// This action can optionally:
/// - release PlayerAnchorState when the status is removed
/// - stop later actions in the chain
///
/// That is what lets a bed work cleanly:
/// - first use: apply Resting, then allow later anchor/snap actions to run
/// - second use: remove Resting, release to exit anchor, stop the chain
/// </summary>
public class ToggleStatusAction : InteractableAction
{
    [Header("Status Toggle")]
    [Tooltip("The status definition to toggle on the interactor.")]
    [SerializeField] private StatusDefinition statusToToggle;

    [Tooltip("If true, stop later actions in the chain when this action removes the status.")]
    [SerializeField] private bool stopFurtherActionsWhenRemoved = true;

    [Tooltip("If true, stop later actions in the chain when this action applies the status.")]
    [SerializeField] private bool stopFurtherActionsWhenApplied = false;

    [Tooltip("If true, release the player's anchor state when this action removes the status.")]
    [SerializeField] private bool releaseAnchorOnRemove = false;

    [Tooltip("Optional duration override, in seconds, when applying a timed status. Leave negative to use the status definition default.")]
    [SerializeField] private float durationOverrideSeconds = -1f;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (context.InteractorStatusManager == null)
        {
            Debug.LogWarning("[ToggleStatusAction] Interactor has no StatusManager.", context.InteractorObject);
            return false;
        }

        if (statusToToggle == null)
        {
            Debug.LogWarning("[ToggleStatusAction] No statusToToggle assigned.", this);
            return false;
        }

        return true;
    }

    public override void Execute(InteractionContext context)
    {
    	Debug.Log($"[ToggleStatusAction] Toggling status '{statusToToggle.DisplayName}'.");
        StatusManager statusManager = context.InteractorStatusManager;

        // -----------------------------------------------------------------
        // REMOVE PATH
        // -----------------------------------------------------------------
        if (statusManager.HasStatus(statusToToggle))
        {
            bool removed = statusManager.RemoveStatus(statusToToggle);

            if (removed)
            {
                Debug.Log($"[ToggleStatusAction] Removed status '{statusToToggle.DisplayName}'.");

                if (releaseAnchorOnRemove && context.InteractorAnchorState != null)
                {
                    context.InteractorAnchorState.ReleaseToExitAnchor();
                }

                if (stopFurtherActionsWhenRemoved)
                {
                    context.StopFurtherActions = true;
                }
            }

            return;
        }

        // -----------------------------------------------------------------
        // APPLY PATH
        // -----------------------------------------------------------------
        bool applied = statusManager.ApplyStatus(statusToToggle, 1, durationOverrideSeconds);

        if (applied)
        {
            Debug.Log($"[ToggleStatusAction] Applied status '{statusToToggle.DisplayName}'.");

            if (stopFurtherActionsWhenApplied)
            {
                context.StopFurtherActions = true;
            }
        }
    }
}