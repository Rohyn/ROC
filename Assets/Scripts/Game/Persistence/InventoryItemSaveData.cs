using System;

namespace ROC.Persistence
{
    [Serializable]
    public class InventoryItemSaveData
    {
        public string ItemId;
        public int Quantity;
    }
}