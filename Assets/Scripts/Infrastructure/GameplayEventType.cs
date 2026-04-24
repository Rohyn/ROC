/// <summary>
/// Normalized gameplay event types that quests can consume.
///
/// IMPORTANT:
/// Quests should react to these normalized events rather than directly
/// inspecting arbitrary game systems. This keeps authored, generated,
/// and repeatable quests all using the same runtime model.
/// </summary>
public enum GameplayEventType
{
    None = 0,

    // Quest lifecycle
    QuestAccepted = 1,
    QuestCompleted = 2,
    QuestTurnedIn = 3,

    // Social / conversation
    TalkedToNpc = 10,

    // World interaction
    InteractedWithObject = 20,
    EnteredLocation = 21,

    // Inventory / equipment
    ItemAddedToInventory = 30,
    ItemRemovedFromInventory = 31,
    ItemEquipped = 32,
    ItemUnequipped = 33,

    // Expression / abilities
    EmotePerformed = 40,
    AbilityUsed = 41,
    TargetHit = 42,

    // Combat / gathering / crafting
    EntityKilled = 50,
    ResourceHarvested = 51,
    ItemCrafted = 52
}