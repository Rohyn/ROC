using UnityEngine;

namespace ROC.Inventory
{
    /// <summary>
    /// Authored definition for an inventory item.
    ///
    /// This is intentionally very small for now.
    /// It is enough to support:
    /// - a unique item ID
    /// - a player-facing name
    /// - max stack size
    ///
    /// For the infirmary key, set Max Stack to 1.
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

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public int MaxStack => maxStack;

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