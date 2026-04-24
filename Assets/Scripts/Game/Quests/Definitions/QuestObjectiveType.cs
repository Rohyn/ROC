/// <summary>
/// Supported objective types for first-pass questing.
///
/// This list is meant to be broad enough to support:
/// - tutorial/story quests
/// - repeatable jobs
/// - procedurally generated collection/combat tasks
///
/// IMPORTANT:
/// More types can be added later without rewriting the quest runtime model.
/// </summary>
public enum QuestObjectiveType
{
    None = 0,

    TalkToNpc = 1,
    InteractWithObject = 2,
    EnterLocation = 3,

    PossessItem = 10,
    EquipItem = 11,

    PerformEmote = 20,
    UseAbility = 21,
    HitTarget = 22,

    KillEntity = 30,
    HarvestResource = 31,
    CraftItem = 32,

    CompleteQuestWithTag = 40,
    AcceptQuestWithTag = 41
}