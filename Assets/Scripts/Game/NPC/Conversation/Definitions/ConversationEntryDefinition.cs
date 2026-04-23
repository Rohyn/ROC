using System;
using UnityEngine;

/// <summary>
/// One authored response entry inside an NPC conversation profile.
///
/// Multiple entries can share the same topic.
/// The resolver will choose the highest-priority entry whose conditions pass.
/// </summary>
[Serializable]
public class ConversationEntryDefinition
{
    [Header("Identity")]
    public string entryId;

    [Header("Topic")]
    public ConversationTopicDefinition topic;

    [Header("Response")]
    [TextArea(2, 8)]
    public string responseText;

    [Header("Selection")]
    public int priority = 0;
    public ConversationEntryConditionSet conditions;

    [Header("Follow-up Topics")]
    [Tooltip("If empty, conversation falls back to the profile's root topics.")]
    public ConversationTopicDefinition[] followUpTopics;
}