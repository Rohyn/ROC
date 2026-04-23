using UnityEngine;
using ROC.Inventory;

/// <summary>
/// Grants one or more progression flags to the interacting player.
///
/// This is intentionally separate from other interaction actions.
/// That keeps tutorial/quest/ritual flow logic modular.
///
/// IMPORTANT:
/// For the infirmary door flow:
/// - attach this AFTER RotateInteractAction
/// - set RotateInteractAction.stopFurtherActionsOnFailure = false
/// - configure this action to grant intro.tried_infirmary_door
///   only if the player is missing the infirmary key
/// </summary>
public class GrantProgressFlagAction : InteractableAction
{
    [Header("Flags To Grant")]
    [Tooltip("Progress flags to grant to the interactor.")]
    [SerializeField] private string[] flagsToGrant;

    [Header("Optional Conditions")]
    [Tooltip("Only grant flags if the player is missing ANY of these items in their bag.")]
    [SerializeField] private ItemDefinition[] onlyIfMissingAnyBagItems;

    [Header("Flow Control")]
    [Tooltip("If true, stop later actions after this action grants at least one flag.")]
    [SerializeField] private bool stopFurtherActionsAfterGrant = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (context.InteractorProgressState == null)
        {
            return false;
        }

        if (flagsToGrant == null || flagsToGrant.Length == 0)
        {
            return false;
        }

        // If no additional condition is configured, allow execution.
        if (onlyIfMissingAnyBagItems == null || onlyIfMissingAnyBagItems.Length == 0)
        {
            return true;
        }

        if (context.InteractorInventory == null)
        {
            return false;
        }

        for (int i = 0; i < onlyIfMissingAnyBagItems.Length; i++)
        {
            ItemDefinition item = onlyIfMissingAnyBagItems[i];
            if (item == null)
            {
                continue;
            }

            if (!context.InteractorInventory.HasItem(item, PlayerInventory.InventoryCollection.Bag, 1))
            {
                return true;
            }
        }

        return false;
    }

    public override void Execute(InteractionContext context)
    {
        if (context == null || context.InteractorProgressState == null)
        {
            return;
        }

        int grantedCount = context.InteractorProgressState.GrantFlags(flagsToGrant);

        if (verboseLogging)
        {
            Debug.Log($"[GrantProgressFlagAction] Granted {grantedCount} progress flag(s).", this);
        }

        if (grantedCount > 0 && stopFurtherActionsAfterGrant)
        {
            context.StopFurtherActions = true;
        }
    }
}