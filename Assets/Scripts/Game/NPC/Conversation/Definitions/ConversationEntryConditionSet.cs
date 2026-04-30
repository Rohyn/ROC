using System;
using System.Collections.Generic;
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

    [Header("Debug")]
    [SerializeField] private bool verboseConditionLogging = true;

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

        if (!CheckQuestState(questLog, interactorObject))
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

    private bool CheckQuestState(PlayerQuestLog questLog, GameObject interactorObject)
    {
        if (HasAnyValidId(requiredActiveQuestIds) && questLog == null)
        {
            Log(interactorObject, "Failed quest conditions: required active quests exist, but player has no PlayerQuestLog.");
            return false;
        }

        if (HasAnyValidId(requiredCompletedQuestIds) && questLog == null)
        {
            Log(interactorObject, "Failed quest conditions: required completed quests exist, but player has no PlayerQuestLog.");
            return false;
        }

        if (requiredActiveQuestIds != null)
        {
            for (int i = 0; i < requiredActiveQuestIds.Length; i++)
            {
                string questId = NormalizeId(requiredActiveQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (!IsQuestActive(questLog, questId))
                {
                    Log(interactorObject, $"Failed quest conditions: required active quest '{questId}' is not active.");
                    return false;
                }
            }
        }

        if (requiredCompletedQuestIds != null)
        {
            for (int i = 0; i < requiredCompletedQuestIds.Length; i++)
            {
                string questId = NormalizeId(requiredCompletedQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (!IsQuestCompleted(questLog, questId))
                {
                    Log(interactorObject, $"Failed quest conditions: required completed quest '{questId}' is not completed.");
                    return false;
                }
            }
        }

        if (blockedActiveQuestIds != null)
        {
            for (int i = 0; i < blockedActiveQuestIds.Length; i++)
            {
                string questId = NormalizeId(blockedActiveQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (IsQuestActive(questLog, questId))
                {
                    Log(interactorObject, $"Failed quest conditions: blocked active quest '{questId}' is already active.");
                    return false;
                }
            }
        }

        if (blockedCompletedQuestIds != null)
        {
            for (int i = 0; i < blockedCompletedQuestIds.Length; i++)
            {
                string questId = NormalizeId(blockedCompletedQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (IsQuestCompleted(questLog, questId))
                {
                    Log(interactorObject, $"Failed quest conditions: blocked completed quest '{questId}' is already completed.");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsQuestActive(PlayerQuestLog questLog, string questId)
    {
        if (questLog == null || string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        string normalizedQuestId = NormalizeId(questId);

        IReadOnlyList<QuestInstance> activeQuests = questLog.ActiveQuests;

        if (activeQuests == null)
        {
            return false;
        }

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];

            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            if (IdsMatch(instance.Definition.QuestId, normalizedQuestId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQuestCompleted(PlayerQuestLog questLog, string questId)
    {
        if (questLog == null || string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        string normalizedQuestId = NormalizeId(questId);

        IReadOnlyList<string> completedQuestIds = questLog.CompletedQuestDefinitionIds;

        if (completedQuestIds == null)
        {
            return false;
        }

        for (int i = 0; i < completedQuestIds.Count; i++)
        {
            if (IdsMatch(completedQuestIds[i], normalizedQuestId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyValidId(string[] ids)
    {
        if (ids == null)
        {
            return false;
        }

        for (int i = 0; i < ids.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(ids[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IdsMatch(string a, string b)
    {
        return string.Equals(NormalizeId(a), NormalizeId(b), StringComparison.Ordinal);
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private void Log(GameObject context, string message)
    {
        if (!verboseConditionLogging)
        {
            return;
        }

        Debug.Log($"[ConversationEntryConditionSet] {message}", context);
    }
}