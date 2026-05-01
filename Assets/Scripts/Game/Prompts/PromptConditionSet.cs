using System;
using ROC.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reusable read-only condition set for prompt eligibility.
/// Mirrors the same player-facing state categories used by conversation conditions.
/// </summary>
[Serializable]
public class PromptConditionSet
{
    [Header("Conversation")]
    [SerializeField] private bool suppressWhileConversationOpen = true;

    [Header("Required Loaded Scenes")]
    [Tooltip("If populated, at least one listed scene must currently be loaded.")]
    [SerializeField] private string[] requiredLoadedSceneNames;

    [Header("Blocked Loaded Scenes")]
    [Tooltip("If any listed scene is loaded, this prompt is blocked.")]
    [SerializeField] private string[] blockedLoadedSceneNames;

    [Header("Required Progress Flags")]
    [SerializeField] private string[] requiredProgressFlags;

    [Header("Blocked Progress Flags")]
    [SerializeField] private string[] blockedProgressFlags;

    [Header("Required Bag Item IDs")]
    [SerializeField] private string[] requiredBagItemIds;

    [Header("Required Equipped Item IDs")]
    [SerializeField] private string[] requiredEquippedItemIds;

    [Header("Required Active Quests")]
    [SerializeField] private string[] requiredActiveQuestIds;

    [Header("Required Completed Quests")]
    [SerializeField] private string[] requiredCompletedQuestIds;

    [Header("Blocked Active Quests")]
    [SerializeField] private string[] blockedActiveQuestIds;

    [Header("Blocked Completed Quests")]
    [SerializeField] private string[] blockedCompletedQuestIds;

    public bool IsSatisfiedBy(GameObject playerObject)
    {
        if (playerObject == null)
        {
            return false;
        }

        return IsSatisfiedBy(
            playerObject.GetComponent<PlayerProgressState>(),
            playerObject.GetComponent<PlayerInventory>(),
            playerObject.GetComponent<PlayerQuestLog>(),
            playerObject.GetComponent<PlayerConversationState>());
    }

    public bool IsSatisfiedBy(
        PlayerProgressState progressState,
        PlayerInventory inventory,
        PlayerQuestLog questLog,
        PlayerConversationState conversationState)
    {
        if (suppressWhileConversationOpen && conversationState != null && conversationState.IsConversationOpen)
        {
            return false;
        }

        return CheckLoadedScenes()
            && CheckProgressFlags(progressState)
            && CheckInventory(inventory)
            && CheckQuestState(questLog);
    }

    private bool CheckLoadedScenes()
    {
        if (HasAnyValidId(requiredLoadedSceneNames))
        {
            bool foundRequired = false;

            for (int i = 0; i < requiredLoadedSceneNames.Length; i++)
            {
                string sceneName = NormalizeId(requiredLoadedSceneNames[i]);

                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    continue;
                }

                Scene scene = SceneManager.GetSceneByName(sceneName);

                if (scene.IsValid() && scene.isLoaded)
                {
                    foundRequired = true;
                    break;
                }
            }

            if (!foundRequired)
            {
                return false;
            }
        }

        if (HasAnyValidId(blockedLoadedSceneNames))
        {
            for (int i = 0; i < blockedLoadedSceneNames.Length; i++)
            {
                string sceneName = NormalizeId(blockedLoadedSceneNames[i]);

                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    continue;
                }

                Scene scene = SceneManager.GetSceneByName(sceneName);

                if (scene.IsValid() && scene.isLoaded)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool CheckProgressFlags(PlayerProgressState progressState)
    {
        if (HasAnyValidId(requiredProgressFlags))
        {
            if (progressState == null || !progressState.HasAllFlags(requiredProgressFlags))
            {
                return false;
            }
        }

        if (HasAnyValidId(blockedProgressFlags))
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
        if (HasAnyValidId(requiredBagItemIds))
        {
            if (inventory == null)
            {
                return false;
            }

            for (int i = 0; i < requiredBagItemIds.Length; i++)
            {
                string itemId = NormalizeId(requiredBagItemIds[i]);

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                if (!inventory.HasItemById(itemId, PlayerInventory.InventoryCollection.Bag, 1))
                {
                    return false;
                }
            }
        }

        if (HasAnyValidId(requiredEquippedItemIds))
        {
            if (inventory == null)
            {
                return false;
            }

            for (int i = 0; i < requiredEquippedItemIds.Length; i++)
            {
                string itemId = NormalizeId(requiredEquippedItemIds[i]);

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                if (!inventory.HasItemById(itemId, PlayerInventory.InventoryCollection.Equipped, 1))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool CheckQuestState(PlayerQuestLog questLog)
    {
        if (HasAnyValidId(requiredActiveQuestIds) && questLog == null)
        {
            return false;
        }

        if (HasAnyValidId(requiredCompletedQuestIds) && questLog == null)
        {
            return false;
        }

        if (HasAnyValidId(requiredActiveQuestIds))
        {
            for (int i = 0; i < requiredActiveQuestIds.Length; i++)
            {
                string questId = NormalizeId(requiredActiveQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (!IsQuestActiveClientAware(questLog, questId))
                {
                    return false;
                }
            }
        }

        if (HasAnyValidId(requiredCompletedQuestIds))
        {
            for (int i = 0; i < requiredCompletedQuestIds.Length; i++)
            {
                string questId = NormalizeId(requiredCompletedQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (!IsQuestCompletedClientAware(questLog, questId))
                {
                    return false;
                }
            }
        }

        if (HasAnyValidId(blockedActiveQuestIds) && questLog != null)
        {
            for (int i = 0; i < blockedActiveQuestIds.Length; i++)
            {
                string questId = NormalizeId(blockedActiveQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (IsQuestActiveClientAware(questLog, questId))
                {
                    return false;
                }
            }
        }

        if (HasAnyValidId(blockedCompletedQuestIds) && questLog != null)
        {
            for (int i = 0; i < blockedCompletedQuestIds.Length; i++)
            {
                string questId = NormalizeId(blockedCompletedQuestIds[i]);

                if (string.IsNullOrWhiteSpace(questId))
                {
                    continue;
                }

                if (IsQuestCompletedClientAware(questLog, questId))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsQuestActiveClientAware(PlayerQuestLog questLog, string questId)
    {
        if (questLog == null || string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        if (questLog.HasActiveQuest(questId))
        {
            return true;
        }

        if (questLog.JournalActiveQuests == null)
        {
            return false;
        }

        for (int i = 0; i < questLog.JournalActiveQuests.Count; i++)
        {
            QuestJournalEntryData entry = questLog.JournalActiveQuests[i];

            if (entry == null)
            {
                continue;
            }

            if (IdsMatch(entry.QuestId, questId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQuestCompletedClientAware(PlayerQuestLog questLog, string questId)
    {
        if (questLog == null || string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        if (questLog.HasCompletedQuest(questId))
        {
            return true;
        }

        if (questLog.JournalCompletedQuestIds == null)
        {
            return false;
        }

        for (int i = 0; i < questLog.JournalCompletedQuestIds.Count; i++)
        {
            if (IdsMatch(questLog.JournalCompletedQuestIds[i], questId))
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
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
