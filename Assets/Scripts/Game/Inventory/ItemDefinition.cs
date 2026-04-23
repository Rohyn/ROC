using UnityEngine;

namespace ROC.Inventory
{
    /// <summary>
    /// Authored definition for an inventory item.
    ///
    /// This version adds a simple "equippable" flag so the inventory can decide
    /// whether an item may be moved into the equipped state.
    ///
    /// For now:
    /// - Shirt / Pants: IsEquippable = true
    /// - Infirmary Key: IsEquippable = false
    ///
    /// Equipment slot restrictions can be added later.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ItemDefinition",
        menuName = "ROC/Inventory/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string itemId = "new_item";
        [SerializeField] private string displayName = "New Item";

        [Header("Inventory")]
        [Min(1)]
        [SerializeField] private int maxStack = 1;

        [Header("Equipment")]
        [SerializeField] private bool isEquippable = false;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public int MaxStack => maxStack;
        public bool IsEquippable => isEquippable;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                itemId = "new_item";
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }

            if (maxStack < 1)
            {
                maxStack = 1;
            }
        }
    }
}