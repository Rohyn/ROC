using System;
using System.Collections;
using System.Collections.Generic;
using ROC.Inventory;
using UnityEngine;

/// <summary>
/// Owner-local observer that turns replicated inventory changes into small notices.
///
/// It diffs stable inventory snapshots, so PlayerInventory does not need to expose
/// new typed events for this first pass.
/// </summary>
[DisallowMultipleComponent]
public class InventoryNotificationBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerNotificationController notificationController;

    [Tooltip("Optional. Used to suppress equip/unequip notices while the inventory panel is visible.")]
    [SerializeField] private InventoryPanelView inventoryPanelView;

    [Header("Display")]
    [SerializeField] private bool showItemGainNotices = true;
    [SerializeField] private bool showItemLossNotices = true;
    [SerializeField] private bool showEquipNotices = true;
    [SerializeField] private bool showUnequipNotices = true;

    [Header("Inventory Panel Suppression")]
    [Tooltip("If true, equipped/unequipped notices are hidden while the inventory panel is visible. Item gain/loss notices still display.")]
    [SerializeField] private bool suppressEquipNoticesWhileInventoryPanelVisible = true;

    [Header("Timing")]
    [Tooltip("Short debounce lets bag/equipment NetworkList changes settle before diffing.")]
    [SerializeField] private float evaluationDelaySeconds = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerInventory _inventory;
    private InventorySnapshot _lastSnapshot;
    private Coroutine _pendingEvaluationRoutine;
    private bool _hasBaseline;

    private void OnEnable()
    {
        TryBindDependencies();
    }

    private void OnDisable()
    {
        UnbindInventory();

        if (_pendingEvaluationRoutine != null)
        {
            StopCoroutine(_pendingEvaluationRoutine);
            _pendingEvaluationRoutine = null;
        }
    }

    private void Update()
    {
        if (_inventory == null || notificationController == null || inventoryPanelView == null)
        {
            TryBindDependencies();
        }
    }

    private void TryBindDependencies()
    {
        if (notificationController == null)
        {
            notificationController = FindFirstObjectByType<PlayerNotificationController>();
        }

        if (inventoryPanelView == null)
        {
            inventoryPanelView = FindFirstObjectByType<InventoryPanelView>();
        }

        if (_inventory != null)
        {
            return;
        }

        PlayerInventory[] inventories = FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);

        for (int i = 0; i < inventories.Length; i++)
        {
            PlayerInventory candidate = inventories[i];

            if (candidate == null || !candidate.IsOwner)
            {
                continue;
            }

            BindInventory(candidate);
            break;
        }
    }

    private void BindInventory(PlayerInventory inventory)
    {
        if (_inventory == inventory)
        {
            return;
        }

        UnbindInventory();

        _inventory = inventory;
        _inventory.InventoryChanged += HandleInventoryChanged;

        _lastSnapshot = CaptureSnapshot(_inventory);
        _hasBaseline = true;

        if (verboseLogging)
        {
            Debug.Log("[InventoryNotificationBridge] Bound local PlayerInventory.", this);
        }
    }

    private void UnbindInventory()
    {
        if (_inventory != null)
        {
            _inventory.InventoryChanged -= HandleInventoryChanged;
            _inventory = null;
        }

        _hasBaseline = false;
        _lastSnapshot = null;
    }

    private void HandleInventoryChanged()
    {
        if (_pendingEvaluationRoutine != null)
        {
            StopCoroutine(_pendingEvaluationRoutine);
        }

        _pendingEvaluationRoutine = StartCoroutine(EvaluateAfterDelayRoutine());
    }

    private IEnumerator EvaluateAfterDelayRoutine()
    {
        float delay = Mathf.Max(0f, evaluationDelaySeconds);

        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }
        else
        {
            yield return null;
        }

        _pendingEvaluationRoutine = null;
        EvaluateInventoryChanges();
    }

    private void EvaluateInventoryChanges()
    {
        if (_inventory == null)
        {
            return;
        }

        InventorySnapshot newSnapshot = CaptureSnapshot(_inventory);

        if (!_hasBaseline || _lastSnapshot == null)
        {
            _lastSnapshot = newSnapshot;
            _hasBaseline = true;
            return;
        }

        EmitDiffNotices(_lastSnapshot, newSnapshot);
        _lastSnapshot = newSnapshot;
    }

    private void EmitDiffNotices(InventorySnapshot oldSnapshot, InventorySnapshot newSnapshot)
    {
        if (notificationController == null || oldSnapshot == null || newSnapshot == null)
        {
            return;
        }

        bool suppressEquipUnequip =
            suppressEquipNoticesWhileInventoryPanelVisible &&
            inventoryPanelView != null &&
            inventoryPanelView.IsVisible();

        HashSet<string> itemIds = new HashSet<string>();

        foreach (string itemId in oldSnapshot.ItemIds)
        {
            itemIds.Add(itemId);
        }

        foreach (string itemId in newSnapshot.ItemIds)
        {
            itemIds.Add(itemId);
        }

        foreach (string itemId in itemIds)
        {
            InventoryObservedItem oldItem = oldSnapshot.Get(itemId);
            InventoryObservedItem newItem = newSnapshot.Get(itemId);

            string displayName = ResolveDisplayName(oldItem, newItem, itemId);

            int oldBag = oldItem != null ? oldItem.BagQuantity : 0;
            int newBag = newItem != null ? newItem.BagQuantity : 0;
            int oldEquipped = oldItem != null ? oldItem.EquippedQuantity : 0;
            int newEquipped = newItem != null ? newItem.EquippedQuantity : 0;

            int bagDelta = newBag - oldBag;
            int equippedDelta = newEquipped - oldEquipped;

            int equipAmount = Math.Min(Math.Max(-bagDelta, 0), Math.Max(equippedDelta, 0));
            int unequipAmount = Math.Min(Math.Max(bagDelta, 0), Math.Max(-equippedDelta, 0));

            int remainingBagDelta = bagDelta + equipAmount - unequipAmount;

            if (!suppressEquipUnequip && showEquipNotices && equipAmount > 0)
            {
                notificationController.EnqueueInventoryNotice(
                    "Equipped",
                    FormatItem(displayName, equipAmount),
                    priority: 350);
            }

            if (!suppressEquipUnequip && showUnequipNotices && unequipAmount > 0)
            {
                notificationController.EnqueueInventoryNotice(
                    "Unequipped",
                    FormatItem(displayName, unequipAmount),
                    priority: 300);
            }

            if (showItemGainNotices && remainingBagDelta > 0)
            {
                notificationController.EnqueueInventoryNotice(
                    $"+{remainingBagDelta}",
                    displayName,
                    priority: 250);
            }

            if (showItemLossNotices && remainingBagDelta < 0)
            {
                notificationController.EnqueueInventoryNotice(
                    $"-{Math.Abs(remainingBagDelta)}",
                    displayName,
                    priority: 225);
            }

            if (verboseLogging && suppressEquipUnequip && (equipAmount > 0 || unequipAmount > 0))
            {
                Debug.Log("[InventoryNotificationBridge] Suppressed equip/unequip notice because inventory panel is visible.", this);
            }
        }
    }

    private static InventorySnapshot CaptureSnapshot(PlayerInventory inventory)
    {
        InventorySnapshot snapshot = new InventorySnapshot();

        if (inventory == null)
        {
            return snapshot;
        }

        AddCollectionToSnapshot(inventory, PlayerInventory.InventoryCollection.Bag, snapshot);
        AddCollectionToSnapshot(inventory, PlayerInventory.InventoryCollection.Equipped, snapshot);

        return snapshot;
    }

    private static void AddCollectionToSnapshot(
        PlayerInventory inventory,
        PlayerInventory.InventoryCollection collection,
        InventorySnapshot snapshot)
    {
        int count = inventory.GetEntryCount(collection);

        for (int i = 0; i < count; i++)
        {
            if (!inventory.TryGetDisplayInfoAt(
                    i,
                    collection,
                    out string itemId,
                    out string displayName,
                    out int quantity,
                    out bool isEquippable))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            {
                continue;
            }

            snapshot.AddOrUpdate(
                itemId.Trim(),
                displayName,
                quantity,
                collection,
                isEquippable);
        }
    }

    private static string ResolveDisplayName(
        InventoryObservedItem oldItem,
        InventoryObservedItem newItem,
        string fallbackItemId)
    {
        if (newItem != null && !string.IsNullOrWhiteSpace(newItem.DisplayName))
        {
            return newItem.DisplayName;
        }

        if (oldItem != null && !string.IsNullOrWhiteSpace(oldItem.DisplayName))
        {
            return oldItem.DisplayName;
        }

        return fallbackItemId ?? string.Empty;
    }

    private static string FormatItem(string displayName, int quantity)
    {
        if (quantity <= 1)
        {
            return displayName;
        }

        return $"{displayName} x{quantity}";
    }

    private sealed class InventorySnapshot
    {
        private readonly Dictionary<string, InventoryObservedItem> _items = new();

        public IEnumerable<string> ItemIds => _items.Keys;

        public InventoryObservedItem Get(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            _items.TryGetValue(itemId.Trim(), out InventoryObservedItem item);
            return item;
        }

        public void AddOrUpdate(
            string itemId,
            string displayName,
            int quantity,
            PlayerInventory.InventoryCollection collection,
            bool isEquippable)
        {
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            {
                return;
            }

            string normalizedItemId = itemId.Trim();

            if (!_items.TryGetValue(normalizedItemId, out InventoryObservedItem item) || item == null)
            {
                item = new InventoryObservedItem(normalizedItemId);
                _items[normalizedItemId] = item;
            }

            item.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedItemId : displayName;
            item.IsEquippable = isEquippable;

            if (collection == PlayerInventory.InventoryCollection.Equipped)
            {
                item.EquippedQuantity += quantity;
            }
            else
            {
                item.BagQuantity += quantity;
            }
        }
    }

    private sealed class InventoryObservedItem
    {
        public string ItemId { get; }
        public string DisplayName { get; set; }
        public int BagQuantity { get; set; }
        public int EquippedQuantity { get; set; }
        public bool IsEquippable { get; set; }

        public InventoryObservedItem(string itemId)
        {
            ItemId = itemId;
            DisplayName = itemId;
        }
    }
}
