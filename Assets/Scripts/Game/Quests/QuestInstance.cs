using System;
using System.Collections.Generic;
using ROC.Persistence;
using UnityEngine;

/// <summary>
/// Runtime instance of a quest owned by one player.
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

        public QuestObjectiveProgressData(
            string objectiveId,
            int currentCount,
            bool isComplete)
        {
            this.objectiveId = objectiveId;
            this.currentCount = Mathf.Max(0, currentCount);
            this.isComplete = isComplete;
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

        public QuestObjectiveProgressSaveData CreateSaveData()
        {
            return new QuestObjectiveProgressSaveData
            {
                ObjectiveId = objectiveId,
                CurrentCount = currentCount,
                IsComplete = isComplete
            };
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

        InitializeEmptyObjectiveProgress();
    }

    public QuestInstance(QuestDefinition definition, QuestSaveData saveData)
    {
        this.definition = definition;

        instanceId = saveData != null && !string.IsNullOrWhiteSpace(saveData.InstanceId)
            ? saveData.InstanceId
            : Guid.NewGuid().ToString("N");

        state = saveData != null && IsValidState(saveData.State)
            ? (QuestInstanceState)saveData.State
            : QuestInstanceState.Active;

        RestoreObjectiveProgress(saveData);
    }

    public QuestSaveData CreateSaveData()
    {
        QuestSaveData saveData = new QuestSaveData
        {
            QuestId = definition != null ? definition.QuestId : string.Empty,
            InstanceId = instanceId,
            State = (int)state,
            Objectives = new List<QuestObjectiveProgressSaveData>()
        };

        if (objectiveProgress != null)
        {
            for (int i = 0; i < objectiveProgress.Length; i++)
            {
                QuestObjectiveProgressData progress = objectiveProgress[i];

                if (progress == null)
                {
                    continue;
                }

                saveData.Objectives.Add(progress.CreateSaveData());
            }
        }

        return saveData;
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

    public bool ApplyGameplayEvent(GameplayEventData eventData, GameObject playerObject)
    {
        if (definition == null || definition.Objectives == null || objectiveProgress == null)
        {
            return false;
        }

        bool changed = false;

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

    private void InitializeEmptyObjectiveProgress()
    {
        QuestObjectiveDefinition[] objectives = definition != null ? definition.Objectives : null;
        int count = objectives != null ? objectives.Length : 0;

        objectiveProgress = new QuestObjectiveProgressData[count];

        for (int i = 0; i < count; i++)
        {
            string objectiveId = objectives[i] != null ? objectives[i].ObjectiveId : $"objective_{i}";
            objectiveProgress[i] = new QuestObjectiveProgressData(objectiveId);
        }
    }

    private void RestoreObjectiveProgress(QuestSaveData saveData)
    {
        QuestObjectiveDefinition[] objectives = definition != null ? definition.Objectives : null;
        int count = objectives != null ? objectives.Length : 0;

        objectiveProgress = new QuestObjectiveProgressData[count];

        for (int i = 0; i < count; i++)
        {
            string objectiveId = objectives[i] != null ? objectives[i].ObjectiveId : $"objective_{i}";
            QuestObjectiveProgressSaveData savedProgress = FindSavedObjectiveProgress(saveData, objectiveId);

            if (savedProgress != null)
            {
                objectiveProgress[i] = new QuestObjectiveProgressData(
                    objectiveId,
                    savedProgress.CurrentCount,
                    savedProgress.IsComplete);
            }
            else
            {
                objectiveProgress[i] = new QuestObjectiveProgressData(objectiveId);
            }
        }
    }

    private static QuestObjectiveProgressSaveData FindSavedObjectiveProgress(
        QuestSaveData saveData,
        string objectiveId)
    {
        if (saveData == null || saveData.Objectives == null || string.IsNullOrWhiteSpace(objectiveId))
        {
            return null;
        }

        for (int i = 0; i < saveData.Objectives.Count; i++)
        {
            QuestObjectiveProgressSaveData progress = saveData.Objectives[i];

            if (progress == null)
            {
                continue;
            }

            if (progress.ObjectiveId == objectiveId)
            {
                return progress;
            }
        }

        return null;
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

    private static bool IsValidState(int stateValue)
    {
        return Enum.IsDefined(typeof(QuestInstanceState), stateValue);
    }
}