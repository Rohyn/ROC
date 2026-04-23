using UnityEngine;

/// <summary>
/// Interaction action that records anchor-related state on the interactor.
///
/// This action does NOT move the player by itself.
/// It simply tells the player's PlayerAnchorState:
/// - what object now owns the anchor relationship
/// - what the current anchor transform is
/// - what exit anchor should be used when released
///
/// This is intended to be used together with:
/// - SnapToAnchorAction
/// - ToggleStatusAction
///
/// Example:
/// A bed might have these actions in order:
/// 1. ToggleStatusAction (Resting)
/// 2. SetAnchorStateAction
/// 3. SnapToAnchorAction
/// </summary>
public class SetAnchorStateAction : InteractableAction
{
    [Header("Anchor State")]
    [Tooltip("The main anchor transform associated with this interaction. Usually the same transform used by SnapToAnchorAction.")]
    [SerializeField] private Transform anchor;

    [Tooltip("Optional exit anchor used when the player is released from this anchored state.")]
    [SerializeField] private Transform exitAnchor;

    [Tooltip("Optional explicit owner object for the anchor relationship. If left empty, this GameObject is used.")]
    [SerializeField] private GameObject anchorOwnerOverride;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (context.InteractorAnchorState == null)
        {
            Debug.LogWarning("[SetAnchorStateAction] Interactor has no PlayerAnchorState.", context.InteractorObject);
            return false;
        }

        if (anchor == null)
        {
            Debug.LogWarning("[SetAnchorStateAction] No anchor assigned.", this);
            return false;
        }

        return true;
    }

    public override void Execute(InteractionContext context)
    {
    	Debug.Log("[SetAnchorStateAction] Setting anchor state.");
        GameObject anchorOwner = anchorOwnerOverride != null ? anchorOwnerOverride : gameObject;

        context.InteractorAnchorState.SetAnchor(anchorOwner, anchor, exitAnchor);

        Debug.Log("[SetAnchorStateAction] Anchor state set on interactor.");
    }
}