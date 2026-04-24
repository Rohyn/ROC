using UnityEngine;

/// <summary>
/// Authored quest definition.
///
/// This single definition type can represent:
/// - hand-authored storyline quests
/// - repeatable jobs
/// - later, generated quests after template resolution
///
/// IMPORTANT:
/// The runtime system works with QuestInstance objects, not directly with definitions.
/// </summary>
[CreateAssetMenu(menuName = "ROC/Quests/Quest Definition")]
public class QuestDefinition : ScriptableObject
{
    public enum QuestKind
    {
        Storyline = 0,
        Generated = 1,
        Job = 2
    }

    [Header("Identity")]
    [SerializeField] private string questId = "quest.new";
    [SerializeField] private string title = "New Quest";

    [TextArea(2, 6)]
    [SerializeField] private string description = "Quest description.";

    [SerializeField] private QuestKind questKind = QuestKind.Storyline;

    [Header("Tags")]
    [Tooltip("Optional category tags included in QuestAccepted and QuestCompleted events.")]
    [SerializeField] private string[] tags;

    [Header("Availability")]
    [SerializeField] private bool repeatable = false;
    [SerializeField] private QuestConditionSet availabilityConditions;

    [Header("Progression")]
    [Tooltip("If true, the quest automatically completes when all objectives are satisfied.")]
    [SerializeField] private bool autoCompleteOnObjectivesMet = true;

    [SerializeField] private QuestObjectiveDefinition[] objectives;

    [Header("Rewards")]
    [SerializeField] private QuestRewardDefinition rewards;

    public string QuestId => questId;
    public string Title => title;
    public string Description => description;
    public QuestKind Kind => questKind;
    public string[] Tags => tags;
    public bool Repeatable => repeatable;
    public QuestConditionSet AvailabilityConditions => availabilityConditions;
    public bool AutoCompleteOnObjectivesMet => autoCompleteOnObjectivesMet;
    public QuestObjectiveDefinition[] Objectives => objectives;
    public QuestRewardDefinition Rewards => rewards;

    public bool CanBeAcceptedBy(GameObject playerObject)
    {
        if (availabilityConditions == null)
        {
            return true;
        }

        return availabilityConditions.IsSatisfiedBy(playerObject);
    }

    public bool HasTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tags == null)
        {
            return false;
        }

        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == tag)
            {
                return true;
            }
        }

        return false;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            questId = "quest.new";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = name;
        }
    }
}