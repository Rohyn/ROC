using System;
using UnityEngine;

/// <summary>
/// Runtime instance of a quest owned by one player.
///
/// IMPORTANT:
/// - A player never directly "has" a QuestDefinition as active state.
/// - They have a QuestInstance, which tracks progress and runtime state.
/// </summary>
[Serializable]
public class QuestInstance
{
    public enum QuestInstanceState
    {
        None = 0,
        Active = 1,
        ReadyToTurnIn = 2,
        Completed = 3,
        Failed = 4,
        Abandoned = 5
    }

    [Serializable]
    public class QuestObjectiveProgressData
    {
        [SerializeField] private string objectiveId;
        [SerializeField] private int currentCount;
        [SerializeField] private bool isComplete;

        public string ObjectiveId => objectiveId;
        public int CurrentCount => currentCount;
        public bool IsComplete => isComplete;

        public QuestObjectiveProgressData(string objectiveId)
        {
            this.objectiveId = objectiveId;
            currentCount = 0;
            isComplete = false;
        }

        public void SetProgress(int count, int required)
        {
            currentCount = Mathf.Max(0, count);
            isComplete = currentCount >= Mathf.Max(1, required);
        }

        public void Increment(int amount, int required)
        {
            currentCount += Mathf.Max(0, amount);
            isComplete = currentCount >= Mathf.Max(1, required);
        }
    }

    [SerializeField] private string instanceId;
    [SerializeField] private QuestDefinition definition;
    [SerializeField] private QuestInstanceState state;
    [SerializeField] private QuestObjectiveProgressData[] objectiveProgress;

    public string InstanceId => instanceId;
    public QuestDefinition Definition => definition;
    public QuestInstanceState State => state;
    public QuestObjectiveProgressData[] ObjectiveProgress => objectiveProgress;

    public QuestInstance(QuestDefinition definition)
    {
        this.definition = definition;
        instanceId = Guid.NewGuid().ToString("N");
        state = QuestInstanceState.Active;

        QuestObjectiveDefinition[] objectives = definition != null ? definition.Objectives : null;
        int count = objectives != null ? objectives.Length : 0;

        objectiveProgress = new QuestObjectiveProgressData[count];

        for (int i = 0; i < count; i++)
        {
            string objectiveId = objectives[i] != null ? objectives[i].ObjectiveId : $"objective_{i}";
            objectiveProgress[i] = new QuestObjectiveProgressData(objectiveId);
        }
    }

    public bool IsAllObjectivesSatisfied()
    {
        if (objectiveProgress == null || objectiveProgress.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < objectiveProgress.Length; i++)
        {
            if (objectiveProgress[i] == null || !objectiveProgress[i].IsComplete)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Initializes or refreshes state-driven objectives from the player's current state.
    /// Use this when the quest is first accepted and after events that might affect
    /// possession/equipment style objectives.
    /// </summary>
    public void RefreshStateDrivenObjectives(GameObject playerObject)
    {
        if (definition == null || definition.Objectives == null || objectiveProgress == null)
        {
            return;
        }

        for (int i = 0; i < definition.Objectives.Length && i < objectiveProgress.Length; i++)
        {
            QuestObjectiveDefinition objective = definition.Objectives[i];
            QuestObjectiveProgressData progress = objectiveProgress[i];

            if (objective == null || progress == null)
            {
                continue;
            }

            if (!objective.IsStateDriven)
            {
                continue;
            }

            int currentValue = objective.GetCurrentProgressFromPlayer(playerObject);
            progress.SetProgress(currentValue, objective.RequiredQuantity);
        }

        EvaluateOverallState();
    }

    /// <summary>
    /// Applies a gameplay event to this quest instance.
    ///
    /// Returns true if any objective progress changed.
    /// </summary>
    public bool ApplyGameplayEvent(GameplayEventData eventData, GameObject playerObject)
    {
        if (definition == null || definition.Objectives == null || objectiveProgress == null)
        {
            return false;
        }

        bool changed = false;

        // Event-driven objective advancement.
        for (int i = 0; i < definition.Objectives.Length && i < objectiveProgress.Length; i++)
        {
            QuestObjectiveDefinition objective = definition.Objectives[i];
            QuestObjectiveProgressData progress = objectiveProgress[i];

            if (objective == null || progress == null)
            {
                continue;
            }

            if (objective.IsStateDriven)
            {
                continue;
            }

            if (progress.IsComplete)
            {
                continue;
            }

            if (!objective.MatchesGameplayEvent(eventData))
            {
                continue;
            }

            int amount = Mathf.Max(1, eventData != null ? eventData.Quantity : 1);
            progress.Increment(amount, objective.RequiredQuantity);
            changed = true;
        }

        // State-driven objectives are recalculated from player state after every event.
        // This keeps possess/equip objectives accurate if items are gained, lost, equipped, or unequipped.
        int stateDrivenBeforeHash = GetStateDrivenCompletionHash();
        RefreshStateDrivenObjectives(playerObject);
        int stateDrivenAfterHash = GetStateDrivenCompletionHash();

        if (stateDrivenBeforeHash != stateDrivenAfterHash)
        {
            changed = true;
        }

        EvaluateOverallState();
        return changed;
    }

    public bool CanTurnIn()
    {
        return state == QuestInstanceState.ReadyToTurnIn;
    }

    public void MarkCompleted()
    {
        state = QuestInstanceState.Completed;
    }

    public void MarkAbandoned()
    {
        state = QuestInstanceState.Abandoned;
    }

    private void EvaluateOverallState()
    {
        if (state == QuestInstanceState.Completed ||
            state == QuestInstanceState.Failed ||
            state == QuestInstanceState.Abandoned)
        {
            return;
        }

        if (!IsAllObjectivesSatisfied())
        {
            state = QuestInstanceState.Active;
            return;
        }

        state = definition != null && definition.AutoCompleteOnObjectivesMet
            ? QuestInstanceState.Completed
            : QuestInstanceState.ReadyToTurnIn;
    }

    private int GetStateDrivenCompletionHash()
    {
        unchecked
        {
            int hash = 17;

            if (definition == null || definition.Objectives == null || objectiveProgress == null)
            {
                return hash;
            }

            for (int i = 0; i < definition.Objectives.Length && i < objectiveProgress.Length; i++)
            {
                QuestObjectiveDefinition objective = definition.Objectives[i];
                QuestObjectiveProgressData progress = objectiveProgress[i];

                if (objective == null || progress == null || !objective.IsStateDriven)
                {
                    continue;
                }

                hash = (hash * 31) + progress.CurrentCount;
                hash = (hash * 31) + (progress.IsComplete ? 1 : 0);
            }

            return hash;
        }
    }
}