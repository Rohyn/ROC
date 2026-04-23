using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ROC.Inventory
{
    /// <summary>
    /// Very basic server-authoritative inventory.
    ///
    /// DESIGN:
    /// - Server owns and mutates inventory state
    /// - Clients receive a replicated read-only view through NetworkList
    ///
    /// CURRENT SCOPE:
    /// - add items
    /// - remove items
    /// - query quantity
    /// - query whether an item can be accepted
    ///
    /// This is intentionally enough to support the infirmary key milestone.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerInventory : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private ItemCatalog itemCatalog;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private readonly NetworkList<ReplicatedInventoryEntry> _items = new();

        public event Action InventoryChanged;

        public override void OnNetworkSpawn()
        {
            _items.OnListChanged += HandleListChanged;
            InventoryChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _items.OnListChanged -= HandleListChanged;
        }

        /// <summary>
        /// Returns true if the inventory can accept the specified quantity of the item.
        ///
        /// For this first version, each item exists as a single logical stack.
        /// If the existing stack is full, the item cannot be accepted.
        /// </summary>
        public bool CanAcceptItem(ItemDefinition itemDefinition, int quantity = 1)
        {
            if (itemDefinition == null || quantity <= 0)
            {
                return false;
            }

            int existingQuantity = GetQuantity(itemDefinition);

            if (itemDefinition.MaxStack <= 0)
            {
                return true;
            }

            return existingQuantity + quantity <= itemDefinition.MaxStack;
        }

        /// <summary>
        /// Server-only add item.
        /// </summary>
        public bool AddItem(ItemDefinition itemDefinition, int quantity = 1)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[PlayerInventory] AddItem was called on a non-server instance.", this);
                return false;
            }

            if (itemDefinition == null || quantity <= 0)
            {
                return false;
            }

            if (!CanAcceptItem(itemDefinition, quantity))
            {
                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Cannot accept {quantity}x '{itemDefinition.DisplayName}'.", this);
                }

                return false;
            }

            FixedString64Bytes itemId = new FixedString64Bytes(itemDefinition.ItemId);

            for (int i = 0; i < _items.Count; i++)
            {
                ReplicatedInventoryEntry entry = _items[i];
                if (!entry.ItemId.Equals(itemId))
                {
                    continue;
                }

                entry.Quantity += quantity;
                _items[i] = entry;

                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Added {quantity}x '{itemDefinition.DisplayName}'. New quantity = {entry.Quantity}.", this);
                }

                return true;
            }

            _items.Add(new ReplicatedInventoryEntry
            {
                ItemId = itemId,
                Quantity = quantity
            });

            if (verboseLogging)
            {
                Debug.Log($"[PlayerInventory] Added new item stack {quantity}x '{itemDefinition.DisplayName}'.", this);
            }

            return true;
        }

        /// <summary>
        /// Server-only remove item.
        /// </summary>
        public bool RemoveItem(ItemDefinition itemDefinition, int quantity = 1)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[PlayerInventory] RemoveItem was called on a non-server instance.", this);
                return false;
            }

            if (itemDefinition == null || quantity <= 0)
            {
                return false;
            }

            FixedString64Bytes itemId = new FixedString64Bytes(itemDefinition.ItemId);

            for (int i = 0; i < _items.Count; i++)
            {
                ReplicatedInventoryEntry entry = _items[i];
                if (!entry.ItemId.Equals(itemId))
                {
                    continue;
                }

                if (entry.Quantity < quantity)
                {
                    return false;
                }

                entry.Quantity -= quantity;

                if (entry.Quantity <= 0)
                {
                    _items.RemoveAt(i);
                }
                else
                {
                    _items[i] = entry;
                }

                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Removed {quantity}x '{itemDefinition.DisplayName}'.", this);
                }

                return true;
            }

            return false;
        }

        public bool HasItem(ItemDefinition itemDefinition, int minimumQuantity = 1)
        {
            return GetQuantity(itemDefinition) >= minimumQuantity;
        }

        public int GetQuantity(ItemDefinition itemDefinition)
        {
            if (itemDefinition == null)
            {
                return 0;
            }

            FixedString64Bytes itemId = new FixedString64Bytes(itemDefinition.ItemId);

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].ItemId.Equals(itemId))
                {
                    return _items[i].Quantity;
                }
            }

            return 0;
        }

        /// <summary>
        /// Returns the number of replicated inventory entries currently visible locally.
        /// Useful for simple UI rendering.
        /// </summary>
        public int GetEntryCount()
        {
            return _items.Count;
        }

        /// <summary>
        /// Returns display-friendly information for an entry at a given index.
        ///
        /// This works on both server and client because it reads the replicated list.
        /// If the item definition cannot be resolved through the catalog, the item ID is used as fallback text.
        /// </summary>
        public bool TryGetDisplayInfoAt(int index, out string itemId, out string displayName, out int quantity)
        {
            itemId = string.Empty;
            displayName = string.Empty;
            quantity = 0;

            if (index < 0 || index >= _items.Count)
            {
                return false;
            }

            ReplicatedInventoryEntry entry = _items[index];

            itemId = entry.ItemId.ToString();
            quantity = entry.Quantity;

            if (itemCatalog != null && itemCatalog.TryGetDefinition(itemId, out ItemDefinition definition) && definition != null)
            {
                displayName = definition.DisplayName;
            }
            else
            {
                displayName = itemId;
            }

            return true;
        }

        private void HandleListChanged(NetworkListEvent<ReplicatedInventoryEntry> changeEvent)
        {
            InventoryChanged?.Invoke();
        }
    }
}