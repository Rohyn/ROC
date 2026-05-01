using System;
using ROC.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reusable read-only condition set for prompt eligibility.
/// Mirrors the same player-facing state categories used by conversation conditions.
///
/// Important distinction:
/// - Loaded scene checks look at Unity's global scene load state.
/// - Current area checks look at the player's logical streamed area through PlayerAreaStreamingController.
///
/// For player-specific tutorial prompts, prefer Required Current Area Scene Names over
/// Required Loaded Scene Names.
///
/// This version also checks PlayerPromptReadinessGate when present, preventing prompts
/// from firing during the short owner-spawn / persistence-restore / area-finalization window.
/// </summary>
[Serializable]
public class PromptConditionSet
{
    [Header("Conversation")]
    [SerializeField] private bool suppressWhileConversationOpen = true;

    [Header("Prompt Startup Readiness")]
    [Tooltip("If true, prompts will fail closed while PlayerPromptReadinessGate says owner-local restored state is not settled yet.")]
    [SerializeField] private bool requirePromptReadinessWhenGateExists = true;

    [Header("Required Current Area Scene Names")]
    [Tooltip("If populated, the owning player must logically be in one of these PlayerAreaStreamingController current areas. Prefer this for streamed area prompts.")]
    [SerializeField] private string[] requiredCurrentAreaSceneNames;

    [Header("Blocked Current Area Scene Names")]
    [Tooltip("If the owning player is logically in any of these areas, this prompt is blocked.")]
    [SerializeField] private string[] blockedCurrentAreaSceneNames;

    [Header("Required Loaded Scenes")]
    [Tooltip("If populated, at least one listed Unity scene must currently be loaded. Use sparingly; this is not player-specific.")]
    [SerializeField] private string[] requiredLoadedSceneNames;

    [Header("Blocked Loaded Scenes")]
    [Tooltip("If any listed Unity scene is loaded, this prompt is blocked. Use sparingly; this is not player-specific.")]
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
            playerObject.GetComponent<PlayerConversationState>(),
            playerObject.GetComponent<PlayerAreaStreamingController>(),
            playerObject.GetComponent<PlayerPromptReadinessGate>());
    }

    public bool IsSatisfiedBy(
        PlayerProgressState progressState,
        PlayerInventory inventory,
        PlayerQuestLog questLog,
        PlayerConversationState conversationState)
    {
        PlayerAreaStreamingController areaStreaming = ResolveAreaStreamingController(
            progressState,
            inventory,
            questLog,
            conversationState);

        PlayerPromptReadinessGate promptReadinessGate = ResolvePromptReadinessGate(
            progressState,
            inventory,
            questLog,
            conversationState,
            areaStreaming);

        return IsSatisfiedBy(
            progressState,
            inventory,
            questLog,
            conversationState,
            areaStreaming,
            promptReadinessGate);
    }

    public bool IsSatisfiedBy(
        PlayerProgressState progressState,
        PlayerInventory inventory,
        PlayerQuestLog questLog,
        PlayerConversationState conversationState,
        PlayerAreaStreamingController areaStreaming)
    {
        PlayerPromptReadinessGate promptReadinessGate = ResolvePromptReadinessGate(
            progressState,
            inventory,
            questLog,
            conversationState,
            areaStreaming);

        return IsSatisfiedBy(
            progressState,
            inventory,
            questLog,
            conversationState,
            areaStreaming,
            promptReadinessGate);
    }

    public bool IsSatisfiedBy(
        PlayerProgressState progressState,
        PlayerInventory inventory,
        PlayerQuestLog questLog,
        PlayerConversationState conversationState,
        PlayerAreaStreamingController areaStreaming,
        PlayerPromptReadinessGate promptReadinessGate)
    {
        if (suppressWhileConversationOpen &&
            conversationState != null &&
            conversationState.IsConversationOpen)
        {
            return false;
        }

        if (requirePromptReadinessWhenGateExists &&
            promptReadinessGate != null &&
            !promptReadinessGate.IsReadyForPrompts)
        {
            return false;
        }

        return CheckCurrentArea(areaStreaming) &&
               CheckLoadedScenes() &&
               CheckProgressFlags(progressState) &&
               CheckInventory(inventory) &&
               CheckQuestState(questLog);
    }

    private static PlayerAreaStreamingController ResolveAreaStreamingController(
        PlayerProgressState progressState,
        PlayerInventory inventory,
        PlayerQuestLog questLog,
        PlayerConversationState conversationState)
    {
        if (progressState != null)
        {
            PlayerAreaStreamingController areaStreaming =
                progressState.GetComponent<PlayerAreaStreamingController>();

            if (areaStreaming != null)
            {
                return areaStreaming;
            }
        }

        if (inventory != null)
        {
            PlayerAreaStreamingController areaStreaming =
                inventory.GetComponent<PlayerAreaStreamingController>();

            if (areaStreaming != null)
            {
                return areaStreaming;
            }
        }

        if (questLog != null)
        {
            PlayerAreaStreamingController areaStreaming =
                questLog.GetComponent<PlayerAreaStreamingController>();

            if (areaStreaming != null)
            {
                return areaStreaming;
            }
        }

        if (conversationState != null)
        {
            PlayerAreaStreamingController areaStreaming =
                conversationState.GetComponent<PlayerAreaStreamingController>();

            if (areaStreaming != null)
            {
                return areaStreaming;
            }
        }

        return null;
    }

    private static PlayerPromptReadinessGate ResolvePromptReadinessGate(
        PlayerProgressState progressState,
        PlayerInventory inventory,
        PlayerQuestLog questLog,
        PlayerConversationState conversationState,
        PlayerAreaStreamingController areaStreaming)
    {
        if (progressState != null)
        {
            PlayerPromptReadinessGate gate = progressState.GetComponent<PlayerPromptReadinessGate>();

            if (gate != null)
            {
                return gate;
            }
        }

        if (inventory != null)
        {
            PlayerPromptReadinessGate gate = inventory.GetComponent<PlayerPromptReadinessGate>();

            if (gate != null)
            {
                return gate;
            }
        }

        if (questLog != null)
        {
            PlayerPromptReadinessGate gate = questLog.GetComponent<PlayerPromptReadinessGate>();

            if (gate != null)
            {
                return gate;
            }
        }

        if (conversationState != null)
        {
            PlayerPromptReadinessGate gate = conversationState.GetComponent<PlayerPromptReadinessGate>();

            if (gate != null)
            {
                return gate;
            }
        }

        if (areaStreaming != null)
        {
            PlayerPromptReadinessGate gate = areaStreaming.GetComponent<PlayerPromptReadinessGate>();

            if (gate != null)
            {
                return gate;
            }
        }

        return null;
    }

    private bool CheckCurrentArea(PlayerAreaStreamingController areaStreaming)
    {
        bool hasRequiredCurrentAreas = HasAnyValidId(requiredCurrentAreaSceneNames);
        bool hasBlockedCurrentAreas = HasAnyValidId(blockedCurrentAreaSceneNames);

        if (!hasRequiredCurrentAreas && !hasBlockedCurrentAreas)
        {
            return true;
        }

        // If this prompt uses player-specific logical area conditions, fail closed
        // until the player's area state is known and not actively transferring.
        if (areaStreaming == null ||
            !areaStreaming.HasInitializedAreaState ||
            areaStreaming.IsAreaTransferInProgress)
        {
            return false;
        }

        string currentAreaSceneName = NormalizeId(areaStreaming.CurrentAreaSceneName);

        if (string.IsNullOrWhiteSpace(currentAreaSceneName))
        {
            return false;
        }

        if (hasRequiredCurrentAreas)
        {
            bool foundRequiredArea = false;

            for (int i = 0; i < requiredCurrentAreaSceneNames.Length; i++)
            {
                string requiredAreaSceneName = NormalizeId(requiredCurrentAreaSceneNames[i]);

                if (string.IsNullOrWhiteSpace(requiredAreaSceneName))
                {
                    continue;
                }

                if (IdsMatch(currentAreaSceneName, requiredAreaSceneName))
                {
                    foundRequiredArea = true;
                    break;
                }
            }

            if (!foundRequiredArea)
            {
                return false;
            }
        }

        if (hasBlockedCurrentAreas)
        {
            for (int i = 0; i < blockedCurrentAreaSceneNames.Length; i++)
            {
                string blockedAreaSceneName = NormalizeId(blockedCurrentAreaSceneNames[i]);

                if (string.IsNullOrWhiteSpace(blockedAreaSceneName))
                {
                    continue;
                }

                if (IdsMatch(currentAreaSceneName, blockedAreaSceneName))
                {
                    return false;
                }
            }
        }

        return true;
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