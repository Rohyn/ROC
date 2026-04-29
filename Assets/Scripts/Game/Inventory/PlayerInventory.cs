using System;
using System.Collections.Generic;
using ROC.Persistence;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ROC.Inventory
{
    /// <summary>
    /// Basic server-authoritative inventory with support for:
    /// - bag items
    /// - equipped items
    ///
    /// DESIGN:
    /// - Server owns and mutates both collections.
    /// - Clients receive replicated read-only views through NetworkList.
    /// - Client UI can request equip/unequip through RPCs on the owning player's inventory.
    ///
    /// CURRENT SCOPE:
    /// - add/remove items from bags
    /// - move one quantity of an equippable item from bag -> equipped
    /// - move one quantity from equipped -> bag
    ///
    /// PERSISTENCE:
    /// - Save/load methods mutate replicated lists directly.
    /// - Save/load does not emit quest gameplay events.
    ///
    /// INTENTIONAL LIMITATIONS:
    /// - no equipment slot rules yet
    /// - if an item is equippable and you have multiple copies, multiple copies can be equipped
    /// - that is acceptable for this milestone and can be restricted later
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerInventory : NetworkBehaviour
    {
        public enum InventoryCollection
        {
            Bag = 0,
            Equipped = 1
        }

        [Header("References")]
        [SerializeField] private ItemCatalog itemCatalog;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private readonly NetworkList<ReplicatedInventoryEntry> _bagItems = new();
        private readonly NetworkList<ReplicatedInventoryEntry> _equippedItems = new();

        public event Action InventoryChanged;

        public override void OnNetworkSpawn()
        {
            _bagItems.OnListChanged += HandleAnyListChanged;
            _equippedItems.OnListChanged += HandleAnyListChanged;
            InventoryChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _bagItems.OnListChanged -= HandleAnyListChanged;
            _equippedItems.OnListChanged -= HandleAnyListChanged;
        }

        // ---------------------------------------------------------------------
        // Bag item API
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns true if the BAG inventory can accept the specified quantity of the item.
        /// </summary>
        public bool CanAcceptItem(ItemDefinition itemDefinition, int quantity = 1)
        {
            if (itemDefinition == null || quantity <= 0)
            {
                return false;
            }

            int existingQuantity = GetQuantity(itemDefinition, InventoryCollection.Bag);

            if (itemDefinition.MaxStack <= 0)
            {
                return true;
            }

            return existingQuantity + quantity <= itemDefinition.MaxStack;
        }

        /// <summary>
        /// Server-only add item to BAGS.
        /// Emits an item-added quest gameplay event.
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
                    Debug.Log($"[PlayerInventory] Cannot accept {quantity}x '{itemDefinition.DisplayName}' into bags.", this);
                }

                return false;
            }

            bool changed = AddToList(_bagItems, itemDefinition.ItemId, quantity);

            if (changed)
            {
                QuestEventUtility.EmitToPlayer(
                    gameObject,
                    GameplayEventData.CreateItemAddedEvent(itemDefinition.ItemId, quantity));

                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Added {quantity}x '{itemDefinition.DisplayName}' to bags.", this);
                }
            }

            return changed;
        }

        /// <summary>
        /// Server-only remove item from BAGS.
        /// Emits an item-removed quest gameplay event.
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

            bool changed = RemoveFromList(_bagItems, itemDefinition.ItemId, quantity);

            if (changed)
            {
                QuestEventUtility.EmitToPlayer(
                    gameObject,
                    GameplayEventData.CreateItemRemovedEvent(itemDefinition.ItemId, quantity));

                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Removed {quantity}x '{itemDefinition.DisplayName}' from bags.", this);
                }
            }

            return changed;
        }

        /// <summary>
        /// Convenience bag-only check.
        /// </summary>
        public bool HasItem(ItemDefinition itemDefinition, int minimumQuantity = 1)
        {
            return HasItem(itemDefinition, InventoryCollection.Bag, minimumQuantity);
        }

        public bool HasItem(ItemDefinition itemDefinition, InventoryCollection collection, int minimumQuantity = 1)
        {
            return GetQuantity(itemDefinition, collection) >= minimumQuantity;
        }

        public bool HasItemById(string itemId, InventoryCollection collection, int minimumQuantity = 1)
        {
            return GetQuantityByItemId(itemId, collection) >= minimumQuantity;
        }

        public int GetQuantity(ItemDefinition itemDefinition, InventoryCollection collection = InventoryCollection.Bag)
        {
            if (itemDefinition == null)
            {
                return 0;
            }

            return GetQuantityByItemId(itemDefinition.ItemId, collection);
        }

        public int GetQuantityByItemId(string itemId, InventoryCollection collection = InventoryCollection.Bag)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return 0;
            }

            NetworkList<ReplicatedInventoryEntry> list = GetList(collection);
            FixedString64Bytes fixedItemId = new FixedString64Bytes(itemId);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ItemId.Equals(fixedItemId))
                {
                    return list[i].Quantity;
                }
            }

            return 0;
        }

        // ---------------------------------------------------------------------
        // Equip / Unequip
        // ---------------------------------------------------------------------

        /// <summary>
        /// Local/client entry point for requesting an equip by item ID.
        /// If running as host/server, executes directly.
        /// </summary>
        public void RequestEquipItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (IsServer)
            {
                EquipItemServer(itemId);
            }
            else
            {
                RequestEquipItemRpc(itemId);
            }
        }

        /// <summary>
        /// Local/client entry point for requesting an unequip by item ID.
        /// If running as host/server, executes directly.
        /// </summary>
        public void RequestUnequipItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (IsServer)
            {
                UnequipItemServer(itemId);
            }
            else
            {
                RequestUnequipItemRpc(itemId);
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestEquipItemRpc(string itemId)
        {
            EquipItemServer(itemId);
        }

        [Rpc(SendTo.Server)]
        private void RequestUnequipItemRpc(string itemId)
        {
            UnequipItemServer(itemId);
        }

        /// <summary>
        /// Server-only equip logic.
        /// Moves one quantity from bag -> equipped if the item is equippable.
        /// Emits an item-equipped quest gameplay event.
        /// </summary>
        private bool EquipItemServer(string itemId)
        {
            if (!IsServer)
            {
                return false;
            }

            if (!TryResolveItemDefinition(itemId, out ItemDefinition itemDefinition) || itemDefinition == null)
            {
                if (verboseLogging)
                {
                    Debug.LogWarning($"[PlayerInventory] Equip failed. Unknown item '{itemId}'.", this);
                }

                return false;
            }

            if (!itemDefinition.IsEquippable)
            {
                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Equip ignored. '{itemDefinition.DisplayName}' is not equippable.", this);
                }

                return false;
            }

            if (GetQuantity(itemDefinition, InventoryCollection.Bag) < 1)
            {
                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Equip failed. '{itemDefinition.DisplayName}' is not in bags.", this);
                }

                return false;
            }

            bool removedFromBag = RemoveFromList(_bagItems, itemId, 1);
            if (!removedFromBag)
            {
                return false;
            }

            bool addedToEquipped = AddToList(_equippedItems, itemId, 1);

            if (!addedToEquipped)
            {
                // Roll back if something unexpected happened.
                AddToList(_bagItems, itemId, 1);
                return false;
            }

            QuestEventUtility.EmitToPlayer(
                gameObject,
                GameplayEventData.CreateItemEquippedEvent(itemDefinition.ItemId, 1));

            if (verboseLogging)
            {
                Debug.Log($"[PlayerInventory] Equipped '{itemDefinition.DisplayName}'.", this);
            }

            return true;
        }

        /// <summary>
        /// Server-only unequip logic.
        /// Moves one quantity from equipped -> bag.
        /// Emits an item-unequipped quest gameplay event.
        /// </summary>
        private bool UnequipItemServer(string itemId)
        {
            if (!IsServer)
            {
                return false;
            }

            if (!TryResolveItemDefinition(itemId, out ItemDefinition itemDefinition) || itemDefinition == null)
            {
                if (verboseLogging)
                {
                    Debug.LogWarning($"[PlayerInventory] Unequip failed. Unknown item '{itemId}'.", this);
                }

                return false;
            }

            if (GetQuantity(itemDefinition, InventoryCollection.Equipped) < 1)
            {
                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Unequip failed. '{itemDefinition.DisplayName}' is not equipped.", this);
                }

                return false;
            }

            if (!CanAcceptItem(itemDefinition, 1))
            {
                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInventory] Unequip failed. Bags cannot accept '{itemDefinition.DisplayName}'.", this);
                }

                return false;
            }

            bool removedFromEquipped = RemoveFromList(_equippedItems, itemId, 1);
            if (!removedFromEquipped)
            {
                return false;
            }

            bool addedToBag = AddToList(_bagItems, itemId, 1);

            if (!addedToBag)
            {
                // Roll back if something unexpected happened.
                AddToList(_equippedItems, itemId, 1);
                return false;
            }

            QuestEventUtility.EmitToPlayer(
                gameObject,
                GameplayEventData.CreateItemUnequippedEvent(itemDefinition.ItemId, 1));

            if (verboseLogging)
            {
                Debug.Log($"[PlayerInventory] Unequipped '{itemDefinition.DisplayName}'.", this);
            }

            return true;
        }

        // ---------------------------------------------------------------------
        // UI helper API
        // ---------------------------------------------------------------------

        public int GetEntryCount(InventoryCollection collection)
        {
            return GetList(collection).Count;
        }

        public bool TryGetDisplayInfoAt(
            int index,
            InventoryCollection collection,
            out string itemId,
            out string displayName,
            out int quantity,
            out bool isEquippable)
        {
            itemId = string.Empty;
            displayName = string.Empty;
            quantity = 0;
            isEquippable = false;

            NetworkList<ReplicatedInventoryEntry> list = GetList(collection);

            if (index < 0 || index >= list.Count)
            {
                return false;
            }

            ReplicatedInventoryEntry entry = list[index];

            itemId = entry.ItemId.ToString();
            quantity = entry.Quantity;

            if (itemCatalog != null && itemCatalog.TryGetDefinition(itemId, out ItemDefinition definition) && definition != null)
            {
                displayName = definition.DisplayName;
                isEquippable = definition.IsEquippable;
            }
            else
            {
                displayName = itemId;
                isEquippable = false;
            }

            return true;
        }

        // ---------------------------------------------------------------------
        // Persistence API
        // ---------------------------------------------------------------------

        /// <summary>
        /// Server-only save loading.
        /// Replaces current bag/equipped state with saved state.
        ///
        /// Important:
        /// - This does NOT emit quest gameplay events.
        /// - Save loading is state restoration, not gameplay acquisition/equip action.
        /// </summary>
        public void ApplySaveDataServer(
            IReadOnlyList<InventoryItemSaveData> bagItems,
            IReadOnlyList<InventoryItemSaveData> equippedItems)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[PlayerInventory] ApplySaveDataServer called on non-server instance.", this);
                return;
            }

            _bagItems.Clear();
            _equippedItems.Clear();

            LoadSaveItemsIntoList(_bagItems, bagItems);
            LoadSaveItemsIntoList(_equippedItems, equippedItems);

            InventoryChanged?.Invoke();
        }

        public List<InventoryItemSaveData> CreateInventorySaveData(InventoryCollection collection)
        {
            NetworkList<ReplicatedInventoryEntry> list = GetList(collection);
            List<InventoryItemSaveData> result = new List<InventoryItemSaveData>(list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                ReplicatedInventoryEntry entry = list[i];

                string itemId = entry.ItemId.ToString();
                int quantity = entry.Quantity;

                if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
                {
                    continue;
                }

                result.Add(new InventoryItemSaveData
                {
                    ItemId = itemId,
                    Quantity = quantity
                });
            }

            return result;
        }

        // ---------------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------------

        private NetworkList<ReplicatedInventoryEntry> GetList(InventoryCollection collection)
        {
            return collection == InventoryCollection.Equipped ? _equippedItems : _bagItems;
        }

        private bool AddToList(NetworkList<ReplicatedInventoryEntry> list, string itemId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            {
                return false;
            }

            FixedString64Bytes fixedItemId = new FixedString64Bytes(itemId);

            for (int i = 0; i < list.Count; i++)
            {
                ReplicatedInventoryEntry entry = list[i];
                if (!entry.ItemId.Equals(fixedItemId))
                {
                    continue;
                }

                entry.Quantity += quantity;
                list[i] = entry;
                return true;
            }

            list.Add(new ReplicatedInventoryEntry
            {
                ItemId = fixedItemId,
                Quantity = quantity
            });

            return true;
        }

        private bool RemoveFromList(NetworkList<ReplicatedInventoryEntry> list, string itemId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            {
                return false;
            }

            FixedString64Bytes fixedItemId = new FixedString64Bytes(itemId);

            for (int i = 0; i < list.Count; i++)
            {
                ReplicatedInventoryEntry entry = list[i];
                if (!entry.ItemId.Equals(fixedItemId))
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
                    list.RemoveAt(i);
                }
                else
                {
                    list[i] = entry;
                }

                return true;
            }

            return false;
        }

        private void LoadSaveItemsIntoList(
            NetworkList<ReplicatedInventoryEntry> list,
            IReadOnlyList<InventoryItemSaveData> saveItems)
        {
            if (saveItems == null)
            {
                return;
            }

            for (int i = 0; i < saveItems.Count; i++)
            {
                InventoryItemSaveData saveItem = saveItems[i];

                if (saveItem == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(saveItem.ItemId) || saveItem.Quantity <= 0)
                {
                    continue;
                }

                if (itemCatalog != null && !itemCatalog.TryGetDefinition(saveItem.ItemId, out ItemDefinition _))
                {
                    Debug.LogWarning($"[PlayerInventory] Save contains unknown item '{saveItem.ItemId}'. Skipping.", this);
                    continue;
                }

                AddToList(list, saveItem.ItemId, saveItem.Quantity);
            }
        }

        private bool TryResolveItemDefinition(string itemId, out ItemDefinition itemDefinition)
        {
            itemDefinition = null;

            if (itemCatalog == null)
            {
                return false;
            }

            return itemCatalog.TryGetDefinition(itemId, out itemDefinition);
        }

        private void HandleAnyListChanged(NetworkListEvent<ReplicatedInventoryEntry> changeEvent)
        {
            InventoryChanged?.Invoke();
        }
    }
}