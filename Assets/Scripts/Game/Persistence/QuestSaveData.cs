using System;
using System.Collections.Generic;

namespace ROC.Persistence
{
    [Serializable]
    public class QuestSaveData
    {
        public string QuestId;
        public string InstanceId;
        public int State;
        public List<QuestObjectiveProgressSaveData> Objectives = new();
    }
}