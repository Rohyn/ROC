using System;
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

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

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

        if (verboseLogging)
        {
            Debug.LogWarning(
                $"[NPCConversationComponent] No valid entry resolved for topic '{GetTopicDebugName(topic)}' on NPC '{name}'.",
                this);
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
            if (verboseLogging)
            {
                Debug.LogWarning(
                    $"[NPCConversationComponent] Could not find topic id '{topicId}' in profile for NPC '{name}'.",
                    this);
            }

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
            if (verboseLogging)
            {
                Debug.Log($"[NPCConversationComponent] No candidate topics for NPC '{name}'.", this);
            }

            return Array.Empty<ConversationTopicDefinition>();
        }

        List<ConversationTopicDefinition> available = new List<ConversationTopicDefinition>();

        for (int i = 0; i < candidateTopics.Length; i++)
        {
            ConversationTopicDefinition topic = candidateTopics[i];

            if (topic == null)
            {
                if (verboseLogging)
                {
                    Debug.LogWarning($"[NPCConversationComponent] Null topic in candidate list on NPC '{name}'.", this);
                }

                continue;
            }

            if (!topic.IsAvailableFor(interactorObject))
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"[NPCConversationComponent] Topic '{GetTopicDebugName(topic)}' failed topic-level availability on NPC '{name}'.",
                        this);
                }

                continue;
            }

            if (!HasAvailableEntryForTopic(interactorObject, topic))
            {
                continue;
            }

            available.Add(topic);
        }

        available.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

        if (verboseLogging)
        {
            Debug.Log(
                $"[NPCConversationComponent] NPC '{name}' resolved {available.Count} available topic(s).",
                this);
        }

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

                if (TopicIdEquals(topic.TopicId, topicId))
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
                    if (TopicIdEquals(entry.topic.TopicId, topicId))
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

                    if (TopicIdEquals(followUp.TopicId, topicId))
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
        if (conversationProfile == null)
        {
            if (verboseLogging)
            {
                Debug.LogWarning(
                    $"[NPCConversationComponent] NPC '{name}' has no conversation profile.",
                    this);
            }

            return false;
        }

        if (conversationProfile.Entries == null || conversationProfile.Entries.Length == 0)
        {
            if (verboseLogging)
            {
                Debug.LogWarning(
                    $"[NPCConversationComponent] NPC '{name}' has no conversation entries. Topic '{GetTopicDebugName(topic)}' will not be shown.",
                    this);
            }

            return false;
        }

        bool sawMatchingTopic = false;

        for (int i = 0; i < conversationProfile.Entries.Length; i++)
        {
            ConversationEntryDefinition entry = conversationProfile.Entries[i];

            if (entry == null)
            {
                continue;
            }

            if (!TopicsMatch(entry.topic, topic))
            {
                continue;
            }

            sawMatchingTopic = true;

            if (entry.conditions != null && !entry.conditions.IsSatisfiedBy(interactorObject))
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"[NPCConversationComponent] Topic '{GetTopicDebugName(topic)}' has matching entry '{entry.entryId}', but entry conditions failed.",
                        this);
                }

                continue;
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"[NPCConversationComponent] Topic '{GetTopicDebugName(topic)}' has available entry '{entry.entryId}'.",
                    this);
            }

            return true;
        }

        if (verboseLogging)
        {
            if (!sawMatchingTopic)
            {
                Debug.LogWarning(
                    $"[NPCConversationComponent] Topic '{GetTopicDebugName(topic)}' is present, but no matching entry was found in NPC '{name}' profile. " +
                    "Check that the entry Topic field points to the same topic asset or has the same TopicId.",
                    this);
            }
            else
            {
                Debug.Log(
                    $"[NPCConversationComponent] Topic '{GetTopicDebugName(topic)}' had matching entries, but none passed conditions.",
                    this);
            }
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

            if (!TopicsMatch(candidate.topic, topic))
            {
                continue;
            }

            if (candidate.conditions != null && !candidate.conditions.IsSatisfiedBy(interactorObject))
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"[NPCConversationComponent] Candidate entry '{candidate.entryId}' for topic '{GetTopicDebugName(topic)}' failed conditions.",
                        this);
                }

                continue;
            }

            if (best == null || candidate.priority > bestPriority)
            {
                best = candidate;
                bestPriority = candidate.priority;
            }
        }

        if (verboseLogging)
        {
            if (best != null)
            {
                Debug.Log(
                    $"[NPCConversationComponent] Best entry for topic '{GetTopicDebugName(topic)}' is '{best.entryId}' with priority {best.priority}.",
                    this);
            }
            else
            {
                Debug.LogWarning(
                    $"[NPCConversationComponent] No best entry found for topic '{GetTopicDebugName(topic)}'.",
                    this);
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

        if (verboseLogging)
        {
            Debug.Log(
                $"[NPCConversationComponent] Applying effects for entry '{entry.entryId}' on NPC '{name}'.",
                this);
        }

        entry.effects.Apply(interactorObject);
    }

    private static bool TopicsMatch(ConversationTopicDefinition a, ConversationTopicDefinition b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        return TopicIdEquals(a.TopicId, b.TopicId);
    }

    private static bool TopicIdEquals(string a, string b)
    {
        return string.Equals(
            NormalizeTopicId(a),
            NormalizeTopicId(b),
            StringComparison.Ordinal);
    }

    private static string NormalizeTopicId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string GetTopicDebugName(ConversationTopicDefinition topic)
    {
        if (topic == null)
        {
            return "<null>";
        }

        string topicId = string.IsNullOrWhiteSpace(topic.TopicId) ? "<empty-id>" : topic.TopicId;
        string displayName = string.IsNullOrWhiteSpace(topic.DisplayName) ? "<empty-display>" : topic.DisplayName;

        return $"{displayName} ({topicId})";
    }
}