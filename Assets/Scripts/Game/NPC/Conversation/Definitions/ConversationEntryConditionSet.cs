using System;
using ROC.Inventory;
using UnityEngine;

/// <summary>
/// Reusable condition set for topic visibility and response selection.
///
/// This uses the player-facing systems you already built:
/// - PlayerProgressState
/// - PlayerInventory
///
/// Later, this can be extended with:
/// - reputation checks
/// - quest state
/// - service availability
/// - NPC memory reactions
/// </summary>
[Serializable]
public class ConversationEntryConditionSet
{
    [Header("Required Progress Flags")]
    [SerializeField] private string[] requiredProgressFlags;

    [Header("Blocked Progress Flags")]
    [SerializeField] private string[] blockedProgressFlags;

    [Header("Required Bag Items")]
    [SerializeField] private ItemDefinition[] requiredBagItems;

    [Header("Required Equipped Items")]
    [SerializeField] private ItemDefinition[] requiredEquippedItems;

    public bool IsSatisfiedBy(GameObject interactorObject)
    {
        if (interactorObject == null)
        {
            return false;
        }

        PlayerProgressState progressState = interactorObject.GetComponent<PlayerProgressState>();
        PlayerInventory inventory = interactorObject.GetComponent<PlayerInventory>();

        if (requiredProgressFlags != null && requiredProgressFlags.Length > 0)
        {
            if (progressState == null || !progressState.HasAllFlags(requiredProgressFlags))
            {
                return false;
            }
        }

        if (blockedProgressFlags != null && blockedProgressFlags.Length > 0)
        {
            if (progressState != null && progressState.HasAnyFlag(blockedProgressFlags))
            {
                return false;
            }
        }

        if (requiredBagItems != null && requiredBagItems.Length > 0)
        {
            if (inventory == null)
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

                if (!inventory.HasItem(item, PlayerInventory.InventoryCollection.Bag, 1))
                {
                    return false;
                }
            }
        }

        if (requiredEquippedItems != null && requiredEquippedItems.Length > 0)
        {
            if (inventory == null)
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

                if (!inventory.HasItem(item, PlayerInventory.InventoryCollection.Equipped, 1))
                {
                    return false;
                }
            }
        }

        return true;
    }
}