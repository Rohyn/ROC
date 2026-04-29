using System;
using System.Collections.Generic;

namespace ROC.Persistence
{
    [Serializable]
    public class AccountSaveData
    {
        public string AccountId;
        public int Potential;
        public int Inspiration;
        public List<string> CharacterIds = new();
    }
}