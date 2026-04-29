using System;
using ROC.Inventory;
using UnityEngine;

/// <summary>
/// Reusable condition set for topic visibility and response selection.
///
/// This uses the player-facing systems:
/// - PlayerProgressState
/// - PlayerInventory
/// - PlayerQuestLog
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

    [Header("Required Active Quests")]
    [SerializeField] private string[] requiredActiveQuestIds;

    [Header("Required Completed Quests")]
    [SerializeField] private string[] requiredCompletedQuestIds;

    [Header("Blocked Active Quests")]
    [SerializeField] private string[] blockedActiveQuestIds;

    [Header("Blocked Completed Quests")]
    [SerializeField] private string[] blockedCompletedQuestIds;

    public bool IsSatisfiedBy(GameObject interactorObject)
    {
        if (interactorObject == null)
        {
            return false;
        }

        PlayerProgressState progressState = interactorObject.GetComponent<PlayerProgressState>();
        PlayerInventory inventory = interactorObject.GetComponent<PlayerInventory>();
        PlayerQuestLog questLog = interactorObject.GetComponent<PlayerQuestLog>();

        if (!CheckProgressFlags(progressState))
        {
            return false;
        }

        if (!CheckInventory(inventory))
        {
            return false;
        }

        if (!CheckQuestState(questLog))
        {
            return false;
        }

        return true;
    }

    private bool CheckProgressFlags(PlayerProgressState progressState)
    {
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

        return true;
    }

    private bool CheckInventory(PlayerInventory inventory)
    {
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

    private bool CheckQuestState(PlayerQuestLog questLog)
    {
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

        return true;
    }
}