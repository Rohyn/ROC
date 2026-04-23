using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// One network-serializable topic option sent from server to owner client.
/// </summary>
public struct ConversationTopicOptionNet : INetworkSerializable
{
    public FixedString64Bytes TopicId;
    public FixedString128Bytes DisplayName;

    public ConversationTopicOptionNet(FixedString64Bytes topicId, FixedString128Bytes displayName)
    {
        TopicId = topicId;
        DisplayName = displayName;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref TopicId);
        serializer.SerializeValue(ref DisplayName);
    }
}

/// <summary>
/// Full conversation payload sent from server to owner client.
/// Uses FixedString fields plus manual serialization of the topic array.
/// </summary>
public struct ConversationStatePayloadNet : INetworkSerializable
{
    public FixedString128Bytes SpeakerName;
    public FixedString4096Bytes ResponseText;
    public ConversationTopicOptionNet[] Topics;

    public ConversationStatePayloadNet(
        FixedString128Bytes speakerName,
        FixedString4096Bytes responseText,
        ConversationTopicOptionNet[] topics)
    {
        SpeakerName = speakerName;
        ResponseText = responseText;
        Topics = topics;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SpeakerName);
        serializer.SerializeValue(ref ResponseText);

        int count = Topics != null ? Topics.Length : 0;
        serializer.SerializeValue(ref count);

        if (serializer.IsReader)
        {
            Topics = count > 0 ? new ConversationTopicOptionNet[count] : new ConversationTopicOptionNet[0];
        }

        for (int i = 0; i < count; i++)
        {
            ConversationTopicOptionNet topic = serializer.IsReader ? default : Topics[i];
            serializer.SerializeValue(ref topic);

            if (serializer.IsReader)
            {
                Topics[i] = topic;
            }
        }
    }
}