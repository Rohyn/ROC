using System;

/// <summary>
/// Runtime conversation response payload returned by an NPC conversation resolver.
///
/// This is what later UI/controllers should consume.
/// </summary>
[Serializable]
public class NPCConversationResponseData
{
    public string speakerName;
    public string responseText;
    public ConversationTopicDefinition[] availableTopics;

    public NPCConversationResponseData(
        string speakerName,
        string responseText,
        ConversationTopicDefinition[] availableTopics)
    {
        this.speakerName = speakerName;
        this.responseText = responseText;
        this.availableTopics = availableTopics;
    }
}