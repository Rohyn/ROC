using System;
using UnityEngine;

/// <summary>
/// Generic gameplay event payload consumed by the quest system.
///
/// IMPORTANT:
/// Quest tags are copied into the Tags field for QuestAccepted / QuestCompleted / QuestTurnedIn events,
/// which lets other quests or achievements react to categories like:
/// - intro-job
/// - combat
/// - local-work
/// </summary>
[Serializable]
public class GameplayEventData
{
    [Header("Core")]
    [SerializeField] private GameplayEventType eventType = GameplayEventType.None;

    [Header("Generic Identifiers")]
    [SerializeField] private string sourceId;
    [SerializeField] private string targetId;

    [Header("Typed Identifiers")]
    [SerializeField] private string npcId;
    [SerializeField] private string itemId;
    [SerializeField] private string locationId;
    [SerializeField] private string interactableId;
    [SerializeField] private string emoteId;
    [SerializeField] private string abilityId;

    [Header("Quest Context")]
    [SerializeField] private string questDefinitionId;
    [SerializeField] private string questInstanceId;

    [Header("Quantities / Metadata")]
    [SerializeField] private int quantity = 1;
    [SerializeField] private string[] tags;

    public GameplayEventType EventType => eventType;
    public string SourceId => sourceId;
    public string TargetId => targetId;
    public string NpcId => npcId;
    public string ItemId => itemId;
    public string LocationId => locationId;
    public string InteractableId => interactableId;
    public string EmoteId => emoteId;
    public string AbilityId => abilityId;
    public string QuestDefinitionId => questDefinitionId;
    public string QuestInstanceId => questInstanceId;
    public int Quantity => quantity;
    public string[] Tags => tags;

    public bool HasTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tags == null)
        {
            return false;
        }

        for (int i = 0; i < tags.Length; i++)
        {
            if (string.Equals(tags[i], tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAnyTag(string[] requiredTags)
    {
        if (requiredTags == null || requiredTags.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < requiredTags.Length; i++)
        {
            if (HasTag(requiredTags[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static GameplayEventData Create(GameplayEventType eventType)
    {
        return new GameplayEventData
        {
            eventType = eventType,
            quantity = 1
        };
    }

    public static GameplayEventData CreateQuestAcceptedEvent(QuestDefinition definition, string questInstanceId)
    {
        return CreateQuestLifecycleEvent(GameplayEventType.QuestAccepted, definition, questInstanceId);
    }

    public static GameplayEventData CreateQuestCompletedEvent(QuestDefinition definition, string questInstanceId)
    {
        return CreateQuestLifecycleEvent(GameplayEventType.QuestCompleted, definition, questInstanceId);
    }

    public static GameplayEventData CreateQuestTurnedInEvent(QuestDefinition definition, string questInstanceId)
    {
        return CreateQuestLifecycleEvent(GameplayEventType.QuestTurnedIn, definition, questInstanceId);
    }

    public static GameplayEventData CreateTalkedToNpcEvent(string npcId)
    {
        return new GameplayEventData
        {
            eventType = GameplayEventType.TalkedToNpc,
            npcId = npcId,
            quantity = 1
        };
    }

    public static GameplayEventData CreateInteractedWithObjectEvent(string interactableId)
    {
        return new GameplayEventData
        {
            eventType = GameplayEventType.InteractedWithObject,
            interactableId = interactableId,
            quantity = 1
        };
    }

    public static GameplayEventData CreateItemAddedEvent(string itemId, int quantity)
    {
        return new GameplayEventData
        {
            eventType = GameplayEventType.ItemAddedToInventory,
            itemId = itemId,
            quantity = Mathf.Max(1, quantity)
        };
    }

    public static GameplayEventData CreateItemRemovedEvent(string itemId, int quantity)
    {
        return new GameplayEventData
        {
            eventType = GameplayEventType.ItemRemovedFromInventory,
            itemId = itemId,
            quantity = Mathf.Max(1, quantity)
        };
    }

    public static GameplayEventData CreateItemEquippedEvent(string itemId, int quantity = 1)
    {
        return new GameplayEventData
        {
            eventType = GameplayEventType.ItemEquipped,
            itemId = itemId,
            quantity = Mathf.Max(1, quantity)
        };
    }

    public static GameplayEventData CreateItemUnequippedEvent(string itemId, int quantity = 1)
    {
        return new GameplayEventData
        {
            eventType = GameplayEventType.ItemUnequipped,
            itemId = itemId,
            quantity = Mathf.Max(1, quantity)
        };
    }

    private static GameplayEventData CreateQuestLifecycleEvent(
        GameplayEventType eventType,
        QuestDefinition definition,
        string questInstanceId)
    {
        return new GameplayEventData
        {
            eventType = eventType,
            questDefinitionId = definition != null ? definition.QuestId : string.Empty,
            thisQuestInstanceId = questInstanceId,
            tags = definition != null ? definition.Tags : null,
            quantity = 1
        };
    }

    private string thisQuestInstanceId
    {
        set => questInstanceId = value;
    }
}