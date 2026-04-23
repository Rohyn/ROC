using UnityEngine;

/// <summary>
/// Authored player-selectable conversation topic.
///
/// Use these as assets instead of enums so topics can scale cleanly,
/// support sub-topics later, and be reused across many NPCs.
/// </summary>
[CreateAssetMenu(menuName = "ROC/NPC/Conversation Topic Definition")]
public class ConversationTopicDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string topicId = "topic.new";
    [SerializeField] private string displayName = "New Topic";

    [Header("Hierarchy")]
    [SerializeField] private ConversationTopicDefinition parentTopic;
    [SerializeField] private int sortOrder = 0;

    [Header("Availability")]
    [SerializeField] private ConversationEntryConditionSet availabilityConditions;

    public string TopicId => topicId;
    public string DisplayName => displayName;
    public ConversationTopicDefinition ParentTopic => parentTopic;
    public int SortOrder => sortOrder;
    public ConversationEntryConditionSet AvailabilityConditions => availabilityConditions;

    public bool IsAvailableFor(GameObject interactorObject)
    {
        if (availabilityConditions == null)
        {
            return true;
        }

        return availabilityConditions.IsSatisfiedBy(interactorObject);
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(topicId))
        {
            topicId = "topic.new";
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = name;
        }
    }
}