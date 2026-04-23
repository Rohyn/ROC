using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Server-authoritative per-player conversation session state.
///
/// FLOW:
/// - StartConversationAction runs on the server and calls StartConversation(...)
/// - this component resolves the opening response through the NPC
/// - server sends the response payload to the owner client
/// - local UI displays speaker name, response text, and topic buttons
/// - player selects a topic
/// - client requests topic resolution from the server
/// - server resolves and sends updated response back to owner
///
/// IMPORTANT:
/// - This component lives on the player prefab
/// - Conversation is per-player, not globally shared
/// - This version auto-closes the conversation if the player moves too far away
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
        // Distance checks must happen on the authoritative server.
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
                Debug.Log("[PlayerConversationState] Active NPC conversation component is missing. Closing conversation.", this);
            }

            CloseConversationServer();
            return;
        }

        float sqrDistance = (_activeNpcConversationServer.transform.position - transform.position).sqrMagnitude;
        float maxSqrDistance = maxConversationDistance * maxConversationDistance;

        if (sqrDistance > maxSqrDistance)
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerConversationState] Player moved too far away from NPC. Closing conversation.", this);
            }

            CloseConversationServer();
        }
    }

    /// <summary>
    /// Server-only start method.
    /// Called by StartConversationAction once the interaction has succeeded.
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

        NPCConversationResponseData response =
            _activeNpcConversationServer.GetOpeningResponse(gameObject);

        PushResponseToOwner(response);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerConversationState] Started conversation with '{npcConversation.DisplayName}'.", this);
        }
    }

    /// <summary>
    /// Client-side request to select a topic.
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
    /// Client-side request to close the current conversation.
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