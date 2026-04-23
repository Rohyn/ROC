using UnityEngine;
using ROC.Inventory;

/// <summary>
/// Gives an inventory item to the interacting player.
///
/// Intended first use:
/// - picking up the infirmary key from the table
///
/// This action assumes the interaction is already being executed on the server.
/// </summary>
public class GiveItemAction : InteractableAction
{
    [Header("Item Grant")]
    [Tooltip("Item definition to give to the interactor.")]
    [SerializeField] private ItemDefinition itemToGive;

    [Min(1)]
    [SerializeField] private int quantity = 1;

    [Tooltip("If true, this action will only execute if the inventory can actually accept the item.")]
    [SerializeField] private bool requireInventorySpace = true;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        if (itemToGive == null)
        {
            Debug.LogWarning("[GiveItemAction] No itemToGive assigned.", this);
            return false;
        }

        if (context.InteractorInventory == null)
        {
            Debug.LogWarning("[GiveItemAction] Interactor has no PlayerInventory.", context.InteractorObject);
            return false;
        }

        if (requireInventorySpace && !context.InteractorInventory.CanAcceptItem(itemToGive, quantity))
        {
            return false;
        }

        return true;
    }

    public override void Execute(InteractionContext context)
    {
        if (context.InteractorInventory == null || itemToGive == null)
        {
            return;
        }

        bool added = context.InteractorInventory.AddItem(itemToGive, quantity);

        if (added)
        {
            Debug.Log($"[GiveItemAction] Granted {quantity}x '{itemToGive.DisplayName}'.");
        }
        else
        {
            Debug.LogWarning($"[GiveItemAction] Failed to grant {quantity}x '{itemToGive.DisplayName}'.", this);
        }
    }
}