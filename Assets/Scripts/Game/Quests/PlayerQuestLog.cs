using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative quest log for one player.
///
/// RESPONSIBILITIES:
/// - accept quests
/// - track active runtime quest instances
/// - track completed quest history by definition id
/// - consume normalized gameplay events
/// - auto-complete quests when appropriate
/// - emit quest accept/complete gameplay events, including quest tags
///
/// IMPORTANT:
/// This is the foundation. The next step after this is wiring game systems to emit
/// GameplayEventData into this quest log.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerQuestLog : NetworkBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    [SerializeField] private List<QuestInstance> activeQuests = new();
    [SerializeField] private List<string> completedQuestDefinitionIds = new();

    public event Action QuestLogChanged;

    public IReadOnlyList<QuestInstance> ActiveQuests => activeQuests;
    public IReadOnlyList<string> CompletedQuestDefinitionIds => completedQuestDefinitionIds;

    public bool HasActiveQuest(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];
            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            if (instance.Definition.QuestId == questId)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasCompletedQuest(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        for (int i = 0; i < completedQuestDefinitionIds.Count; i++)
        {
            if (completedQuestDefinitionIds[i] == questId)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryAcceptQuest(QuestDefinition definition)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] TryAcceptQuest called on non-server instance.", this);
            return false;
        }

        if (definition == null)
        {
            return false;
        }

        if (!definition.Repeatable)
        {
            if (HasActiveQuest(definition.QuestId) || HasCompletedQuest(definition.QuestId))
            {
                return false;
            }
        }
        else
        {
            if (HasActiveQuest(definition.QuestId))
            {
                return false;
            }
        }

        if (!definition.CanBeAcceptedBy(gameObject))
        {
            return false;
        }

        QuestInstance instance = new QuestInstance(definition);
        instance.RefreshStateDrivenObjectives(gameObject);

        activeQuests.Add(instance);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerQuestLog] Accepted quest '{definition.Title}' ({definition.QuestId}).", this);
        }

        // Emit a quest-accepted event with the quest's tags included.
        RecordGameplayEventInternal(GameplayEventData.CreateQuestAcceptedEvent(definition, instance.InstanceId));

        // The accept event itself may advance other active quests that care about accepted quest tags.
        // Also check whether this newly accepted quest immediately qualifies as complete.
        ProcessCompletedStates();

        RaiseQuestLogChanged();
        return true;
    }

    public bool TryTurnInQuest(string questId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] TryTurnInQuest called on non-server instance.", this);
            return false;
        }

        QuestInstance instance = FindActiveQuestInstance(questId);
        if (instance == null || !instance.CanTurnIn())
        {
            return false;
        }

        CompleteQuestInternal(instance, emitQuestCompletedEvent: true, emitQuestTurnedInEvent: true);
        RaiseQuestLogChanged();
        return true;
    }

    public bool TryAbandonQuest(string questId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] TryAbandonQuest called on non-server instance.", this);
            return false;
        }

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];
            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            if (instance.Definition.QuestId != questId)
            {
                continue;
            }

            instance.MarkAbandoned();
            activeQuests.RemoveAt(i);

            if (verboseLogging)
            {
                Debug.Log($"[PlayerQuestLog] Abandoned quest '{questId}'.", this);
            }

            RaiseQuestLogChanged();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Server-only entry point for all gameplay event ingestion.
    /// This is the core of the quest runtime model.
    /// </summary>
    public void RecordGameplayEvent(GameplayEventData eventData)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerQuestLog] RecordGameplayEvent called on non-server instance.", this);
            return;
        }

        RecordGameplayEventInternal(eventData);
        ProcessCompletedStates();
        RaiseQuestLogChanged();
    }

    private void RecordGameplayEventInternal(GameplayEventData eventData)
    {
        if (eventData == null)
        {
            return;
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerQuestLog] Processing gameplay event '{eventData.EventType}'.", this);
        }

        bool anyChanged = false;

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];
            if (instance == null)
            {
                continue;
            }

            bool changed = instance.ApplyGameplayEvent(eventData, gameObject);
            if (changed)
            {
                anyChanged = true;
            }
        }

        if (anyChanged && verboseLogging)
        {
            Debug.Log("[PlayerQuestLog] One or more active quests changed progress.", this);
        }
    }

    private void ProcessCompletedStates()
    {
        // Iterate backwards because completion removes quests from the active list.
        for (int i = activeQuests.Count - 1; i >= 0; i--)
        {
            QuestInstance instance = activeQuests[i];
            if (instance == null)
            {
                activeQuests.RemoveAt(i);
                continue;
            }

            if (instance.State == QuestInstance.QuestInstanceState.Completed)
            {
                CompleteQuestInternal(instance, emitQuestCompletedEvent: true, emitQuestTurnedInEvent: false);
            }
        }
    }

    private void CompleteQuestInternal(
        QuestInstance instance,
        bool emitQuestCompletedEvent,
        bool emitQuestTurnedInEvent)
    {
        if (instance == null || instance.Definition == null)
        {
            return;
        }

        string questId = instance.Definition.QuestId;
        string questTitle = instance.Definition.Title;

        instance.MarkCompleted();

        activeQuests.Remove(instance);

        if (!completedQuestDefinitionIds.Contains(questId))
        {
            completedQuestDefinitionIds.Add(questId);
        }

        if (instance.Definition.Rewards != null)
        {
            instance.Definition.Rewards.ApplyTo(gameObject);
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerQuestLog] Completed quest '{questTitle}' ({questId}).", this);
        }

        if (emitQuestCompletedEvent)
        {
            RecordGameplayEventInternal(GameplayEventData.CreateQuestCompletedEvent(instance.Definition, instance.InstanceId));
        }

        if (emitQuestTurnedInEvent)
        {
            RecordGameplayEventInternal(
                GameplayEventData.CreateQuestTurnedInEvent(instance.Definition, instance.InstanceId));
        }
    }

    private QuestInstance FindActiveQuestInstance(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            return null;
        }

        for (int i = 0; i < activeQuests.Count; i++)
        {
            QuestInstance instance = activeQuests[i];
            if (instance == null || instance.Definition == null)
            {
                continue;
            }

            if (instance.Definition.QuestId == questId)
            {
                return instance;
            }
        }

        return null;
    }

    private void RaiseQuestLogChanged()
    {
        QuestLogChanged?.Invoke();
    }
}