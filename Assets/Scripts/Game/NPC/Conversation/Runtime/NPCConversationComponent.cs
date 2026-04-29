using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime conversation resolver for an NPC.
///
/// This component combines:
/// - resolved NPC identity
/// - authored conversation profile
/// - player state
///
/// Server-side topic resolution can apply entry effects such as accepting quests.
/// </summary>
[RequireComponent(typeof(NPCIdentityComponent))]
public class NPCConversationComponent : MonoBehaviour
{
    [Header("Profile")]
    [SerializeField] private NPCConversationProfile conversationProfile;

    private NPCIdentityComponent identityComponent;

    public string DisplayName => identityComponent != null ? identityComponent.DisplayName : "Unknown";
    public string NpcId => identityComponent != null ? identityComponent.NpcId : string.Empty;
    public NPCConversationProfile ConversationProfile => conversationProfile;

    private void Awake()
    {
        identityComponent = GetComponent<NPCIdentityComponent>();
    }

    public NPCConversationResponseData GetOpeningResponse(GameObject interactorObject)
    {
        string greeting = conversationProfile != null
            ? conversationProfile.OpeningGreeting
            : "Hello.";

        ConversationTopicDefinition[] topics = GetAvailableTopics(
            interactorObject,
            conversationProfile != null ? conversationProfile.RootTopics : null);

        return new NPCConversationResponseData(
            DisplayName,
            greeting,
            topics);
    }

    public NPCConversationResponseData ResolveTopic(GameObject interactorObject, ConversationTopicDefinition topic)
    {
        if (topic == null)
        {
            return GetOpeningResponse(interactorObject);
        }

        ConversationEntryDefinition bestEntry = FindBestEntry(interactorObject, topic);

        if (bestEntry != null)
        {
            ApplyEntryEffects(interactorObject, bestEntry);

            ConversationTopicDefinition[] nextTopics =
                bestEntry.followUpTopics != null && bestEntry.followUpTopics.Length > 0
                    ? GetAvailableTopics(interactorObject, bestEntry.followUpTopics)
                    : GetAvailableTopics(interactorObject, conversationProfile != null ? conversationProfile.RootTopics : null);

            return new NPCConversationResponseData(
                DisplayName,
                bestEntry.responseText,
                nextTopics);
        }

        string fallback = conversationProfile != null
            ? conversationProfile.FallbackResponse
            : "I do not have much to say about that.";

        return new NPCConversationResponseData(
            DisplayName,
            fallback,
            GetAvailableTopics(interactorObject, conversationProfile != null ? conversationProfile.RootTopics : null));
    }

    /// <summary>
    /// Resolves a topic selection by topicId.
    /// This is useful for player conversation UI/network flows that send string IDs.
    /// </summary>
    public NPCConversationResponseData ResolveTopicById(GameObject interactorObject, string topicId)
    {
        if (string.IsNullOrWhiteSpace(topicId))
        {
            return GetOpeningResponse(interactorObject);
        }

        if (!TryFindTopicById(topicId, out ConversationTopicDefinition topic))
        {
            return new NPCConversationResponseData(
                DisplayName,
                conversationProfile != null ? conversationProfile.FallbackResponse : "I do not have much to say about that.",
                GetAvailableTopics(interactorObject, conversationProfile != null ? conversationProfile.RootTopics : null));
        }

        return ResolveTopic(interactorObject, topic);
    }

    public ConversationTopicDefinition[] GetAvailableTopics(
        GameObject interactorObject,
        ConversationTopicDefinition[] candidateTopics)
    {
        if (candidateTopics == null || candidateTopics.Length == 0)
        {
            return new ConversationTopicDefinition[0];
        }

        List<ConversationTopicDefinition> available = new List<ConversationTopicDefinition>();

        for (int i = 0; i < candidateTopics.Length; i++)
        {
            ConversationTopicDefinition topic = candidateTopics[i];

            if (topic == null)
            {
                continue;
            }

            if (!topic.IsAvailableFor(interactorObject))
            {
                continue;
            }

            if (!HasAvailableEntryForTopic(interactorObject, topic))
            {
                continue;
            }

            available.Add(topic);
        }

        available.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return available.ToArray();
    }

    public bool TryFindTopicById(string topicId, out ConversationTopicDefinition foundTopic)
    {
        foundTopic = null;

        if (conversationProfile == null)
        {
            return false;
        }

        HashSet<ConversationTopicDefinition> seen = new HashSet<ConversationTopicDefinition>();

        if (conversationProfile.RootTopics != null)
        {
            for (int i = 0; i < conversationProfile.RootTopics.Length; i++)
            {
                ConversationTopicDefinition topic = conversationProfile.RootTopics[i];

                if (topic == null || !seen.Add(topic))
                {
                    continue;
                }

                if (topic.TopicId == topicId)
                {
                    foundTopic = topic;
                    return true;
                }
            }
        }

        if (conversationProfile.Entries != null)
        {
            for (int i = 0; i < conversationProfile.Entries.Length; i++)
            {
                ConversationEntryDefinition entry = conversationProfile.Entries[i];

                if (entry == null)
                {
                    continue;
                }

                if (entry.topic != null && seen.Add(entry.topic))
                {
                    if (entry.topic.TopicId == topicId)
                    {
                        foundTopic = entry.topic;
                        return true;
                    }
                }

                if (entry.followUpTopics == null)
                {
                    continue;
                }

                for (int j = 0; j < entry.followUpTopics.Length; j++)
                {
                    ConversationTopicDefinition followUp = entry.followUpTopics[j];

                    if (followUp == null || !seen.Add(followUp))
                    {
                        continue;
                    }

                    if (followUp.TopicId == topicId)
                    {
                        foundTopic = followUp;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool HasAvailableEntryForTopic(GameObject interactorObject, ConversationTopicDefinition topic)
    {
        if (conversationProfile == null || conversationProfile.Entries == null)
        {
            return true;
        }

        for (int i = 0; i < conversationProfile.Entries.Length; i++)
        {
            ConversationEntryDefinition entry = conversationProfile.Entries[i];

            if (entry == null || entry.topic != topic)
            {
                continue;
            }

            if (entry.conditions != null && !entry.conditions.IsSatisfiedBy(interactorObject))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private ConversationEntryDefinition FindBestEntry(GameObject interactorObject, ConversationTopicDefinition topic)
    {
        if (conversationProfile == null || conversationProfile.Entries == null)
        {
            return null;
        }

        ConversationEntryDefinition best = null;
        int bestPriority = int.MinValue;

        ConversationEntryDefinition[] entries = conversationProfile.Entries;

        for (int i = 0; i < entries.Length; i++)
        {
            ConversationEntryDefinition candidate = entries[i];

            if (candidate == null)
            {
                continue;
            }

            if (candidate.topic != topic)
            {
                continue;
            }

            if (candidate.conditions != null && !candidate.conditions.IsSatisfiedBy(interactorObject))
            {
                continue;
            }

            if (best == null || candidate.priority > bestPriority)
            {
                best = candidate;
                bestPriority = candidate.priority;
            }
        }

        return best;
    }

    private void ApplyEntryEffects(GameObject interactorObject, ConversationEntryDefinition entry)
    {
        if (interactorObject == null || entry == null || entry.effects == null)
        {
            return;
        }

        if (!entry.effects.HasAnyEffect)
        {
            return;
        }

        entry.effects.Apply(interactorObject);
    }
}