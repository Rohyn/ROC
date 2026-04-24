using System;
using ROC.Inventory;
using UnityEngine;

/// <summary>
/// Reusable condition set for quest availability and later objective gating.
///
/// This reuses systems already present in the project:
/// - PlayerProgressState
/// - PlayerInventory
/// - PlayerQuestLog
///
/// IMPORTANT:
/// This should stay generic and reusable, not quest-specific.
/// </summary>
[Serializable]
public class QuestConditionSet
{
    [Header("Required Progress Flags")]
    [SerializeField] private string[] requiredProgressFlags;

    [Header("Blocked Progress Flags")]
    [SerializeField] private string[] blockedProgressFlags;

    [Header("Required Bag Items")]
    [SerializeField] private ItemDefinition[] requiredBagItems;

    [Header("Required Equipped Items")]
    [SerializeField] private ItemDefinition[] requiredEquippedItems;

    [Header("Required Completed Quests")]
    [SerializeField] private string[] requiredCompletedQuestIds;

    [Header("Blocked Completed Quests")]
    [SerializeField] private string[] blockedCompletedQuestIds;

    [Header("Required Active Quests")]
    [SerializeField] private string[] requiredActiveQuestIds;

    [Header("Blocked Active Quests")]
    [SerializeField] private string[] blockedActiveQuestIds;

    public bool IsSatisfiedBy(GameObject playerObject)
    {
        if (playerObject == null)
        {
            return false;
        }

        PlayerProgressState progressState = playerObject.GetComponent<PlayerProgressState>();
        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>();
        PlayerQuestLog questLog = playerObject.GetComponent<PlayerQuestLog>();

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

        if (requiredCompletedQuestIds != null && requiredCompletedQuestIds.Length > 0)
        {
            if (questLog == null)
            {
                return false;
            }

            for (int i = 0; i < requiredCompletedQuestIds.Length; i++)
            {
                string questId = requiredCompletedQuestIds[i];
                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (!questLog.HasCompletedQuest(questId))
                {
                    return false;
                }
            }
        }

        if (blockedCompletedQuestIds != null && blockedCompletedQuestIds.Length > 0)
        {
            if (questLog != null)
            {
                for (int i = 0; i < blockedCompletedQuestIds.Length; i++)
                {
                    string questId = blockedCompletedQuestIds[i];
                    if (string.IsNullOrWhiteSpace(questId))
                    {
                        continue;
                    }

                    if (questLog.HasCompletedQuest(questId))
                    {
                        return false;
                    }
                }
            }
        }

        if (requiredActiveQuestIds != null && requiredActiveQuestIds.Length > 0)
        {
            if (questLog == null)
            {
                return false;
            }

            for (int i = 0; i < requiredActiveQuestIds.Length; i++)
            {
                string questId = requiredActiveQuestIds[i];
                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (!questLog.HasActiveQuest(questId))
                {
                    return false;
                }
            }
        }

        if (blockedActiveQuestIds != null && blockedActiveQuestIds.Length > 0)
        {
            if (questLog != null)
            {
                for (int i = 0; i < blockedActiveQuestIds.Length; i++)
                {
                    string questId = blockedActiveQuestIds[i];
                    if (string.IsNullOrWhiteSpace(questId))
                    {
                        continue;
                    }

                    if (questLog.HasActiveQuest(questId))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}