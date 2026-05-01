using System;
using ROC.Inventory;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side bridge from equipment state to progress flags.
///
/// Use this for tutorial/state milestones like:
/// - equipping pants grants intro.dressed
/// - equipping a torch grants cave.has_light
/// - equipping ritual robes grants ritual.ready
///
/// This does not own inventory or equipment logic. It only observes equipped state
/// and grants progress flags when authored conditions are satisfied.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerInventory))]
[RequireComponent(typeof(PlayerProgressState))]
public class EquipmentProgressFlagRules : NetworkBehaviour
{
    [SerializeField] private EquipmentProgressFlagRule[] rules;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private PlayerInventory _inventory;
    private PlayerProgressState _progressState;

    private void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        CacheReferences();

        if (_inventory != null)
        {
            _inventory.InventoryChanged += HandleInventoryChanged;
        }

        if (IsServer)
        {
            EvaluateRules();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_inventory != null)
        {
            _inventory.InventoryChanged -= HandleInventoryChanged;
        }
    }

    private void HandleInventoryChanged()
    {
        if (!IsServer)
        {
            return;
        }

        EvaluateRules();
    }

    private void EvaluateRules()
    {
        if (!IsServer || _inventory == null || _progressState == null || rules == null)
        {
            return;
        }

        for (int i = 0; i < rules.Length; i++)
        {
            EquipmentProgressFlagRule rule = rules[i];

            if (rule == null || !rule.IsValid)
            {
                continue;
            }

            if (!rule.IsSatisfiedBy(_inventory))
            {
                continue;
            }

            int grantedCount = _progressState.GrantFlags(rule.FlagsToGrant);

            if (verboseLogging && grantedCount > 0)
            {
                Debug.Log(
                    $"[EquipmentProgressFlagRules] Granted {grantedCount} progress flag(s) from rule '{rule.RuleName}'.",
                    this);
            }
        }
    }

    private void CacheReferences()
    {
        if (_inventory == null)
        {
            _inventory = GetComponent<PlayerInventory>();
        }

        if (_progressState == null)
        {
            _progressState = GetComponent<PlayerProgressState>();
        }
    }
}

[Serializable]
public class EquipmentProgressFlagRule
{
    [SerializeField] private string ruleName = "New Equipment Progress Rule";

    [Header("Required Equipped Item IDs")]
    [Tooltip("All listed item IDs must be equipped unless Require Any Equipped Item is enabled.")]
    [SerializeField] private string[] requiredEquippedItemIds;

    [Tooltip("If true, any one listed item can satisfy the rule. If false, all listed items are required.")]
    [SerializeField] private bool requireAnyEquippedItem = false;

    [Header("Flags To Grant")]
    [SerializeField] private string[] flagsToGrant;

    public string RuleName => ruleName;
    public string[] FlagsToGrant => flagsToGrant;

    public bool IsValid
    {
        get
        {
            return HasAnyValidString(requiredEquippedItemIds) &&
                   HasAnyValidString(flagsToGrant);
        }
    }

    public bool IsSatisfiedBy(PlayerInventory inventory)
    {
        if (inventory == null || !HasAnyValidString(requiredEquippedItemIds))
        {
            return false;
        }

        if (requireAnyEquippedItem)
        {
            for (int i = 0; i < requiredEquippedItemIds.Length; i++)
            {
                string itemId = Normalize(requiredEquippedItemIds[i]);

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                if (inventory.HasItemById(itemId, PlayerInventory.InventoryCollection.Equipped, 1))
                {
                    return true;
                }
            }

            return false;
        }

        for (int i = 0; i < requiredEquippedItemIds.Length; i++)
        {
            string itemId = Normalize(requiredEquippedItemIds[i]);

            if (string.IsNullOrWhiteSpace(itemId))
            {
                continue;
            }

            if (!inventory.HasItemById(itemId, PlayerInventory.InventoryCollection.Equipped, 1))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAnyValidString(string[] values)
    {
        if (values == null)
        {
            return false;
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}