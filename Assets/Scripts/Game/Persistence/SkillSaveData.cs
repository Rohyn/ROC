using System;

namespace ROC.Persistence
{
    [Serializable]
    public class SkillSaveData
    {
        public string SkillId;
        public bool Learned;
        public int Rank;
        public int Xp;
    }
}