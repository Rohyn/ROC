using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative per-player conversation session state.
///
/// RESPONSIBILITIES:
/// - start a conversation with an NPC
/// - resolve selected topics on the server
/// - send response payloads to the owner client
/// - automatically close the conversation if the player moves too far away
///
/// IMPORTANT:
/// - lives on the player prefab
/// - conversation is per-player, not globally shared
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerConversationState : NetworkBehaviour
{
    [Header("Distance Rules")]
    [Tooltip("If the player moves farther than this from the NPC, the conversation closes automatically.")]
    [SerializeField] private float maxConversationDistance = 4.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private NPCConversationComponent _activeNpcConversationServer;

    private string _speakerName = string.Empty;
    private string _responseText = string.Empty;
    private readonly List<PlayerConversationTopicOption> _availableTopics = new();

    public bool IsConversationOpen { get; private set; }
    public string SpeakerName => _speakerName;
    public string ResponseText => _responseText;
    public IReadOnlyList<PlayerConversationTopicOption> AvailableTopics => _availableTopics;

    public event Action ConversationStateChanged;

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (!IsConversationOpen)
        {
            return;
        }

        if (_activeNpcConversationServer == null)
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerConversationState] Active NPC conversation is missing. Closing conversation.", this);
            }

            CloseConversationServer();
            return;
        }

        float maxDistanceSqr = maxConversationDistance * maxConversationDistance;
        float currentDistanceSqr =
            (_activeNpcConversationServer.transform.position - transform.position).sqrMagnitude;

        if (currentDistanceSqr > maxDistanceSqr)
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerConversationState] Player moved out of conversation range. Closing conversation.", this);
            }

            CloseConversationServer();
        }
    }

    /// <summary>
    /// Server-only.
    /// Called by StartConversationAction after the interaction succeeds.
    /// </summary>
    public void StartConversation(NPCConversationComponent npcConversation)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerConversationState] StartConversation should only be called on the server.", this);
            return;
        }

        if (npcConversation == null)
        {
            return;
        }

        _activeNpcConversationServer = npcConversation;
        IsConversationOpen = true;

        NPCConversationResponseData response =
            _activeNpcConversationServer.GetOpeningResponse(gameObject);

        PushResponseToOwner(response);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerConversationState] Started conversation with '{npcConversation.DisplayName}'.", this);
        }
    }

    /// <summary>
    /// Owner-only request to select a topic.
    /// </summary>
    public void RequestSelectTopic(string topicId)
    {
        if (!IsOwner || !IsConversationOpen || string.IsNullOrWhiteSpace(topicId))
        {
            return;
        }

        if (IsServer)
        {
            SelectTopicServer(topicId);
        }
        else
        {
            RequestSelectTopicRpc(topicId);
        }
    }

    /// <summary>
    /// Owner-only request to close the current conversation.
    /// </summary>
    public void RequestCloseConversation()
    {
        if (!IsOwner)
        {
            return;
        }

        if (IsServer)
        {
            CloseConversationServer();
        }
        else
        {
            RequestCloseConversationRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestSelectTopicRpc(string topicId)
    {
        SelectTopicServer(topicId);
    }

    [Rpc(SendTo.Server)]
    private void RequestCloseConversationRpc()
    {
        CloseConversationServer();
    }

    private void SelectTopicServer(string topicId)
    {
        if (!IsServer || _activeNpcConversationServer == null)
        {
            return;
        }

        NPCConversationResponseData response =
            _activeNpcConversationServer.ResolveTopicById(gameObject, topicId);

        PushResponseToOwner(response);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerConversationState] Resolved topic '{topicId}'.", this);
        }
    }

    private void CloseConversationServer()
    {
        if (!IsServer)
        {
            return;
        }

        _activeNpcConversationServer = null;
        IsConversationOpen = false;
        CloseConversationRpc();

        if (verboseLogging)
        {
            Debug.Log("[PlayerConversationState] Closed conversation.", this);
        }
    }

    private void PushResponseToOwner(NPCConversationResponseData response)
    {
        if (response == null)
        {
            return;
        }

        int topicCount = response.availableTopics != null ? response.availableTopics.Length : 0;
        ConversationTopicOptionNet[] topics = new ConversationTopicOptionNet[topicCount];

        for (int i = 0; i < topicCount; i++)
        {
            ConversationTopicDefinition topic = response.availableTopics[i];
            if (topic == null)
            {
                topics[i] = default;
                continue;
            }

            topics[i] = new ConversationTopicOptionNet(
                new FixedString64Bytes(topic.TopicId ?? string.Empty),
                new FixedString128Bytes(topic.DisplayName ?? string.Empty));
        }

        ConversationStatePayloadNet payload = new ConversationStatePayloadNet(
            new FixedString128Bytes(response.speakerName ?? string.Empty),
            new FixedString4096Bytes(response.responseText ?? string.Empty),
            topics);

        ReceiveConversationStateRpc(payload);
    }

    [Rpc(SendTo.Owner)]
    private void ReceiveConversationStateRpc(ConversationStatePayloadNet payload)
    {
        IsConversationOpen = true;
        _speakerName = payload.SpeakerName.ToString();
        _responseText = payload.ResponseText.ToString();

        _availableTopics.Clear();

        if (payload.Topics != null)
        {
            for (int i = 0; i < payload.Topics.Length; i++)
            {
                string topicId = payload.Topics[i].TopicId.ToString();
                string displayName = payload.Topics[i].DisplayName.ToString();

                if (string.IsNullOrWhiteSpace(topicId))
                {
                    continue;
                }

                _availableTopics.Add(new PlayerConversationTopicOption(topicId, displayName));
            }
        }

        ConversationStateChanged?.Invoke();
    }

    [Rpc(SendTo.Owner)]
    private void CloseConversationRpc()
    {
        IsConversationOpen = false;
        _speakerName = string.Empty;
        _responseText = string.Empty;
        _availableTopics.Clear();

        ConversationStateChanged?.Invoke();
    }
}