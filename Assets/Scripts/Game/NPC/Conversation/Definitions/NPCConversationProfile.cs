using UnityEngine;

/// <summary>
/// Authored conversation profile for an NPC.
///
/// This contains:
/// - opening greeting
/// - fallback response
/// - root topics
/// - authored response entries
///
/// A named NPC like Aidan should usually have one authored conversation profile.
/// </summary>
[CreateAssetMenu(menuName = "ROC/NPC/Conversation Profile")]
public class NPCConversationProfile : ScriptableObject
{
    [Header("Default Text")]
    [TextArea(2, 6)]
    [SerializeField] private string openingGreeting = "Hello.";

    [TextArea(2, 6)]
    [SerializeField] private string fallbackResponse = "I do not have much to say about that.";

    [Header("Topic Structure")]
    [SerializeField] private ConversationTopicDefinition[] rootTopics;

    [Header("Entries")]
    [SerializeField] private ConversationEntryDefinition[] entries;

    public string OpeningGreeting => openingGreeting;
    public string FallbackResponse => fallbackResponse;
    public ConversationTopicDefinition[] RootTopics => rootTopics;
    public ConversationEntryDefinition[] Entries => entries;
}