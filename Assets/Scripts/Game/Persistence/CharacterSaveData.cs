using System;
using System.Collections.Generic;

namespace ROC.Persistence
{
    [Serializable]
    public class CharacterSaveData
    {
        public string CharacterId;
        public string AccountId;
        public string CharacterName;

        public string SceneId;
        public SerializableVector3 Position;
        public float Yaw;

        public List<InventoryItemSaveData> BagItems = new();
        public List<InventoryItemSaveData> EquippedItems = new();
        public List<string> ProgressFlags = new();

        // Ready for the next milestone.
        public List<SkillSaveData> Skills = new();
    }
}