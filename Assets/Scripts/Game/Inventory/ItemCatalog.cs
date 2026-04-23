using System.Collections.Generic;
using UnityEngine;

namespace ROC.Inventory
{
    /// <summary>
    /// Shared lookup asset that maps item IDs to ItemDefinition assets.
    ///
    /// This is the inventory equivalent of your StatusCatalog.
    /// It allows network-replicated item IDs to resolve back into local authored assets.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ItemCatalog",
        menuName = "ROC/Inventory/Item Catalog")]
    public class ItemCatalog : ScriptableObject
    {
        [SerializeField] private ItemDefinition[] definitions;

        private Dictionary<string, ItemDefinition> _byId;

        public IReadOnlyList<ItemDefinition> Definitions => definitions;

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        public bool TryGetDefinition(string itemId, out ItemDefinition definition)
        {
            if (_byId == null)
            {
                RebuildLookup();
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                definition = null;
                return false;
            }

            return _byId.TryGetValue(itemId, out definition);
        }

        private void RebuildLookup()
        {
            _byId = new Dictionary<string, ItemDefinition>();

            if (definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.ItemId))
                {
                    Debug.LogWarning($"[ItemCatalog] Item '{definition.name}' has an empty ItemId.", this);
                    continue;
                }

                if (_byId.ContainsKey(definition.ItemId))
                {
                    Debug.LogWarning($"[ItemCatalog] Duplicate ItemId '{definition.ItemId}' found. Keeping first entry.", this);
                    continue;
                }

                _byId.Add(definition.ItemId, definition);
            }
        }
    }
}