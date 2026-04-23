using UnityEngine;
using ROC.Inventory;

/// <summary>
/// Optional availability gate for a GenericInteractable.
///
/// PURPOSE:
/// - Decide whether the interactable should currently be selectable / promptable / usable
/// - Support flow gates like:
///   - only visible after trying a door
///   - only usable if a specific item is equipped
///   - only available if a flag is present or absent
///
/// IMPORTANT:
/// This affects prompt visibility and interaction availability.
/// It does NOT define the interaction result itself.
/// </summary>
[DisallowMultipleComponent]
public class InteractableAvailabilityRules : MonoBehaviour
{
    [Header("Required Progress Flags")]
    [Tooltip("All of these progress flags must be present.")]
    [SerializeField] private string[] requiredProgressFlags;

    [Header("Blocked Progress Flags")]
    [Tooltip("If any of these progress flags are present, the interactable is unavailable.")]
    [SerializeField] private string[] blockedProgressFlags;

    [Header("Required Bag Items")]
    [Tooltip("All of these items must be present in the player's bags.")]
    [SerializeField] private ItemDefinition[] requiredBagItems;

    [Header("Required Equipped Items")]
    [Tooltip("All of these items must be present in the player's equipped collection.")]
    [SerializeField] private ItemDefinition[] requiredEquippedItems;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    public bool CanInteract(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return false;
        }

        // -------------------------------------------------------------
        // Progress flags
        // -------------------------------------------------------------
        if (requiredProgressFlags != null && requiredProgressFlags.Length > 0)
        {
            if (context.InteractorProgressState == null)
            {
                return false;
            }

            if (!context.InteractorProgressState.HasAllFlags(requiredProgressFlags))
            {
                return false;
            }
        }

        if (blockedProgressFlags != null && blockedProgressFlags.Length > 0)
        {
            if (context.InteractorProgressState != null &&
                context.InteractorProgressState.HasAnyFlag(blockedProgressFlags))
            {
                return false;
            }
        }

        // -------------------------------------------------------------
        // Inventory
        // -------------------------------------------------------------
        if (requiredBagItems != null && requiredBagItems.Length > 0)
        {
            if (context.InteractorInventory == null)
            {
                return false;
            }

            for (int i = 0; i < requiredBagItems.Length; i++)
            {
                ItemDefinition item = requiredBagItems[i];
                if (item == null)
                {
                    continue;
                }

                if (!context.InteractorInventory.HasItem(item, PlayerInventory.InventoryCollection.Bag, 1))
                {
                    return false;
                }
            }
        }

        if (requiredEquippedItems != null && requiredEquippedItems.Length > 0)
        {
            if (context.InteractorInventory == null)
            {
                return false;
            }

            for (int i = 0; i < requiredEquippedItems.Length; i++)
            {
                ItemDefinition item = requiredEquippedItems[i];
                if (item == null)
                {
                    continue;
                }

                if (!context.InteractorInventory.HasItem(item, PlayerInventory.InventoryCollection.Equipped, 1))
                {
                    return false;
                }
            }
        }

        if (verboseLogging)
        {
            Debug.Log($"[InteractableAvailabilityRules] '{name}' passed availability checks.");
        }

        return true;
    }
}