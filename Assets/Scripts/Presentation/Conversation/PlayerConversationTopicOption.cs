using System;

/// <summary>
/// Lightweight local UI/runtime topic option.
/// This is not networked directly.
/// </summary>
[Serializable]
public class PlayerConversationTopicOption
{
    public string topicId;
    public string displayName;

    public PlayerConversationTopicOption(string topicId, string displayName)
    {
        this.topicId = topicId;
        this.displayName = displayName;
    }
}