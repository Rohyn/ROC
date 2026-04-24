using System;
using ROC.Inventory;
using UnityEngine;

/// <summary>
/// One objective inside a quest definition.
///
/// DESIGN:
/// - objectives match normalized gameplay events
/// - some objective types are event-driven
/// - some objective types are state-driven (for example PossessItem / EquipItem)
///
/// IMPORTANT:
/// State-driven objectives should be recalculated from the player state,
/// not only incremented by events.
/// </summary>
[Serializable]
public class QuestObjectiveDefinition
{
    [Header("Identity")]
    [SerializeField] private string objectiveId = "objective.new";

    [TextArea(1, 3)]
    [SerializeField] private string description = "New objective";

    [Header("Type")]
    [SerializeField] private QuestObjectiveType objectiveType = QuestObjectiveType.None;

    [Header("Matching")]
    [Tooltip("Primary target identifier. Meaning depends on objective type.")]
    [SerializeField] private string targetId;

    [Tooltip("Optional additional tags required on matching events. Match succeeds if ANY listed tag is present.")]
    [SerializeField] private string[] requiredEventTags;

    [Min(1)]
    [SerializeField] private int requiredQuantity = 1;

    public string ObjectiveId => objectiveId;
    public string Description => description;
    public QuestObjectiveType ObjectiveType => objectiveType;
    public string TargetId => targetId;
    public string[] RequiredEventTags => requiredEventTags;
    public int RequiredQuantity => Mathf.Max(1, requiredQuantity);

    /// <summary>
    /// Returns true if this objective type should be evaluated from player state
    /// rather than purely by incrementing matching events.
    /// </summary>
    public bool IsStateDriven
    {
        get
        {
            return objectiveType == QuestObjectiveType.PossessItem ||
                   objectiveType == QuestObjectiveType.EquipItem;
        }
    }

    /// <summary>
    /// Returns true if the gameplay event matches this objective's event criteria.
    /// For state-driven objectives, this is usually not used for direct incrementing.
    /// </summary>
    public bool MatchesGameplayEvent(GameplayEventData eventData)
    {
        if (eventData == null)
        {
            return false;
        }

        switch (objectiveType)
        {
            case QuestObjectiveType.TalkToNpc:
                return eventData.EventType == GameplayEventType.TalkedToNpc &&
                       string.Equals(eventData.NpcId, targetId, StringComparison.Ordinal);

            case QuestObjectiveType.InteractWithObject:
                return eventData.EventType == GameplayEventType.InteractedWithObject &&
                       string.Equals(eventData.InteractableId, targetId, StringComparison.Ordinal);

            case QuestObjectiveType.EnterLocation:
                return eventData.EventType == GameplayEventType.EnteredLocation &&
                       string.Equals(eventData.LocationId, targetId, StringComparison.Ordinal);

            case QuestObjectiveType.PerformEmote:
                return eventData.EventType == GameplayEventType.EmotePerformed &&
                       string.Equals(eventData.EmoteId, targetId, StringComparison.Ordinal);

            case QuestObjectiveType.UseAbility:
                return eventData.EventType == GameplayEventType.AbilityUsed &&
                       string.Equals(eventData.AbilityId, targetId, StringComparison.Ordinal);

            case QuestObjectiveType.HitTarget:
                return eventData.EventType == GameplayEventType.TargetHit &&
                       string.Equals(eventData.TargetId, targetId, StringComparison.Ordinal) &&
                       eventData.HasAnyTag(requiredEventTags);

            case QuestObjectiveType.KillEntity:
                return eventData.EventType == GameplayEventType.EntityKilled &&
                       string.Equals(eventData.TargetId, targetId, StringComparison.Ordinal) &&
                       eventData.HasAnyTag(requiredEventTags);

            case QuestObjectiveType.HarvestResource:
                return eventData.EventType == GameplayEventType.ResourceHarvested &&
                       string.Equals(eventData.TargetId, targetId, StringComparison.Ordinal) &&
                       eventData.HasAnyTag(requiredEventTags);

            case QuestObjectiveType.CraftItem:
                return eventData.EventType == GameplayEventType.ItemCrafted &&
                       string.Equals(eventData.ItemId, targetId, StringComparison.Ordinal);

            case QuestObjectiveType.CompleteQuestWithTag:
                return eventData.EventType == GameplayEventType.QuestCompleted &&
                       eventData.HasAnyTag(requiredEventTags);

            case QuestObjectiveType.AcceptQuestWithTag:
                return eventData.EventType == GameplayEventType.QuestAccepted &&
                       eventData.HasAnyTag(requiredEventTags);

            default:
                return false;
        }
    }

    /// <summary>
    /// For state-driven objectives, returns the current progress directly from player state.
    /// For non-state-driven objectives, returns 0.
    /// </summary>
    public int GetCurrentProgressFromPlayer(GameObject playerObject)
    {
        if (playerObject == null)
        {
            return 0;
        }

        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            return 0;
        }

        switch (objectiveType)
        {
            case QuestObjectiveType.PossessItem:
                return inventory.GetQuantityByItemId(targetId, PlayerInventory.InventoryCollection.Bag);

            case QuestObjectiveType.EquipItem:
                return inventory.GetQuantityByItemId(targetId, PlayerInventory.InventoryCollection.Equipped);

            default:
                return 0;
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(objectiveId))
        {
            objectiveId = "objective.new";
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = objectiveId;
        }

        if (requiredQuantity < 1)
        {
            requiredQuantity = 1;
        }
    }
}