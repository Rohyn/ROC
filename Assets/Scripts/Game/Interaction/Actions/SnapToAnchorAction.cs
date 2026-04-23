using UnityEngine;

/// <summary>
/// Generic interaction action that snaps the interactor to an anchor point.
///
/// This is intentionally status-agnostic.
/// It can be reused for:
/// - beds
/// - chairs
/// - ladders
/// - roof access
/// - interrogation chairs
/// - chained wall points
/// - spider webs
/// - ritual circles
/// - teleport arrival anchors
///
/// IMPORTANT:
/// This action only performs the spatial snap.
/// It does NOT apply statuses, teleport across scenes, or otherwise impose meaning.
/// </summary>
public class SnapToAnchorAction : InteractableAction
{
    [Header("Snap Setup")]
    [Tooltip("Where the interactor should be moved when this action runs.")]
    [SerializeField] private Transform anchor;

    [Tooltip("If true, align the interactor's rotation to the anchor's rotation.")]
    [SerializeField] private bool alignRotationToAnchor = true;

    [Tooltip("If true, temporarily disable the interactor's CharacterController while snapping.")]
    [SerializeField] private bool disableCharacterControllerDuringSnap = true;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (anchor == null)
        {
            Debug.LogWarning("[SnapToAnchorAction] No anchor assigned.", this);
            return false;
        }

        if (context.InteractorTransform == null)
        {
            return false;
        }

        return true;
    }

    public override void Execute(InteractionContext context)
    {
    	Debug.Log($"[SnapToAnchorAction] Snapping to anchor '{anchor.name}'.");
        CharacterController characterController = context.InteractorCharacterController;
        bool shouldDisableController =
            disableCharacterControllerDuringSnap &&
            characterController != null &&
            characterController.enabled;

        if (shouldDisableController)
        {
            characterController.enabled = false;
        }

        if (alignRotationToAnchor)
        {
            context.InteractorTransform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }
        else
        {
            context.InteractorTransform.SetPositionAndRotation(
                anchor.position,
                context.InteractorTransform.rotation);
        }

        if (shouldDisableController)
        {
            characterController.enabled = true;
        }

        Debug.Log("[SnapToAnchorAction] Snapped interactor to anchor.");
    }
}