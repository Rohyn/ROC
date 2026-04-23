using UnityEngine;
using ROC.Inventory;

/// <summary>
/// Interaction action that opens/closes/toggles an InteractableRotationState.
///
/// This action also optionally supports:
/// - requiring an inventory item to OPEN
/// - consuming that inventory item when the OPEN succeeds
///
/// That makes it suitable for:
/// - infirmary door requiring the infirmary key
/// - locked chest consuming a key
/// - ordinary door with no key requirement
///
/// IMPORTANT:
/// - This action assumes GenericInteractable.TryInteract is already executing on the server.
/// - For doors/chests, this is usually the only action attached to the interactable.
/// </summary>
public class RotateAction : InteractableAction
{
    [Header("Rotation")]
    [Tooltip("The stateful rotation component to control.")]
    [SerializeField] private InteractableRotationState rotationState;

    [Tooltip("How this interaction should affect the rotation state.")]
    [SerializeField] private InteractableRotationState.RotationCommand rotationCommand =
        InteractableRotationState.RotationCommand.Toggle;

    [Header("Optional Item Requirement")]
    [Tooltip("Optional item required in order to OPEN this object.")]
    [SerializeField] private ItemDefinition requiredItemToOpen;

    [Min(1)]
    [SerializeField] private int requiredItemQuantity = 1;

    [Tooltip("If true, the required item is consumed when an OPEN succeeds.")]
    [SerializeField] private bool consumeRequiredItemOnSuccessfulOpen = false;

    [Header("Failure Behavior")]
    [Tooltip("If true, stop any later actions when this action fails a requirement or cannot change state.")]
    [SerializeField] private bool stopFurtherActionsOnFailure = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    public override bool CanExecute(InteractionContext context)
    {
        // Let Execute handle most validation so it can also stop later actions on failure.
        return rotationState != null && context != null && context.HasInteractorClientId;
    }

    public override void Execute(InteractionContext context)
    {
        if (rotationState == null)
        {
            if (stopFurtherActionsOnFailure)
            {
                context.StopFurtherActions = true;
            }

            return;
        }

        if (!context.HasInteractorClientId)
        {
            if (stopFurtherActionsOnFailure)
            {
                context.StopFurtherActions = true;
            }

            return;
        }

        rotationState.PeekCommandResult(
            context.InteractorClientId,
            rotationCommand,
            out bool willChange,
            out bool resultingOpenState);

        // If this interaction would not change anything, stop here.
        // Example:
        // - door already open
        // - command is OpenOnly
        if (!willChange)
        {
            if (verboseLogging)
            {
                Debug.Log("[RotateAction] Rotation command would not change state.");
            }

            if (stopFurtherActionsOnFailure)
            {
                context.StopFurtherActions = true;
            }

            return;
        }

        // Only require the item when the result would be OPEN.
        if (resultingOpenState && requiredItemToOpen != null)
        {
            if (context.InteractorInventory == null ||
                !context.InteractorInventory.HasItem(requiredItemToOpen, requiredItemQuantity))
            {
                if (verboseLogging)
                {
                    Debug.Log($"[RotateAction] Missing required item '{requiredItemToOpen.DisplayName}'.");
                }

                if (stopFurtherActionsOnFailure)
                {
                    context.StopFurtherActions = true;
                }

                return;
            }
        }

        bool applied = rotationState.TryApplyCommand(
            context.InteractorClientId,
            rotationCommand,
            out bool changed,
            out bool finalOpenState);

        if (!applied || !changed)
        {
            if (stopFurtherActionsOnFailure)
            {
                context.StopFurtherActions = true;
            }

            return;
        }

        // Consume the required item only when the successful result is OPEN.
        if (finalOpenState &&
            consumeRequiredItemOnSuccessfulOpen &&
            requiredItemToOpen != null &&
            context.InteractorInventory != null)
        {
            bool removed = context.InteractorInventory.RemoveItem(requiredItemToOpen, requiredItemQuantity);

            if (verboseLogging)
            {
                Debug.Log(removed
                    ? $"[RotateAction] Consumed {requiredItemQuantity}x '{requiredItemToOpen.DisplayName}'."
                    : $"[RotateAction] Failed to consume required item '{requiredItemToOpen.DisplayName}'.");
            }
        }
    }
}