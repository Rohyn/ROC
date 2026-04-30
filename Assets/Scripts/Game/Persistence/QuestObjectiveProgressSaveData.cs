using System;

namespace ROC.Persistence
{
    [Serializable]
    public class QuestObjectiveProgressSaveData
    {
        public string ObjectiveId;
        public int CurrentCount;
        public bool IsComplete;
    }
}