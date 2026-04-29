using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative per-player progression flag state.
///
/// PURPOSE:
/// - Track non-inventory, non-equipment progression/state flags.
/// - Drive tutorial flow, quest flow, ritual order, unlock steps, etc.
///
/// DESIGN:
/// - Server owns and mutates flags.
/// - Clients receive a replicated read-only view.
/// - Local lookups use a HashSet for fast checks.
///
/// EXAMPLES:
/// - intro.stood_up
/// - intro.tried_infirmary_door
/// - ritual.chapter1.step2
///
/// IMPORTANT:
/// This is intentionally separate from inventory.
/// Hidden inventory items are not the right representation for tutorial/quest flow.
///
/// PERSISTENCE:
/// - Save/load methods restore replicated flag state directly.
/// - Save loading does not represent gameplay progress being newly earned.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerProgressState : NetworkBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly NetworkList<FixedString64Bytes> _replicatedFlags = new();
    private readonly HashSet<string> _localFlags = new();

    public event Action FlagsChanged;

    public override void OnNetworkSpawn()
    {
        _replicatedFlags.OnListChanged += HandleFlagsChanged;
        RebuildLocalFlags();
        FlagsChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _replicatedFlags.OnListChanged -= HandleFlagsChanged;
    }

    public bool HasFlag(string flagId)
    {
        if (string.IsNullOrWhiteSpace(flagId))
        {
            return false;
        }

        return _localFlags.Contains(flagId);
    }

    public bool HasAllFlags(IEnumerable<string> flagIds)
    {
        if (flagIds == null)
        {
            return true;
        }

        foreach (string flagId in flagIds)
        {
            if (string.IsNullOrWhiteSpace(flagId))
            {
                continue;
            }

            if (!_localFlags.Contains(flagId))
            {
                return false;
            }
        }

        return true;
    }

    public bool HasAnyFlag(IEnumerable<string> flagIds)
    {
        if (flagIds == null)
        {
            return false;
        }

        foreach (string flagId in flagIds)
        {
            if (string.IsNullOrWhiteSpace(flagId))
            {
                continue;
            }

            if (_localFlags.Contains(flagId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Server-only.
    /// Grants a flag if it does not already exist.
    /// </summary>
    public bool GrantFlag(string flagId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerProgressState] GrantFlag called on non-server instance.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(flagId))
        {
            return false;
        }

        FixedString64Bytes fixedFlag = new FixedString64Bytes(flagId);

        for (int i = 0; i < _replicatedFlags.Count; i++)
        {
            if (_replicatedFlags[i].Equals(fixedFlag))
            {
                return false;
            }
        }

        _replicatedFlags.Add(fixedFlag);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerProgressState] Granted flag '{flagId}'.", this);
        }

        return true;
    }

    /// <summary>
    /// Server-only.
    /// Grants all valid flags in the provided collection.
    /// </summary>
    public int GrantFlags(IEnumerable<string> flagIds)
    {
        if (!IsServer || flagIds == null)
        {
            return 0;
        }

        int granted = 0;

        foreach (string flagId in flagIds)
        {
            if (GrantFlag(flagId))
            {
                granted++;
            }
        }

        return granted;
    }

    /// <summary>
    /// Server-only.
    /// Revokes a single flag if present.
    /// </summary>
    public bool RevokeFlag(string flagId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerProgressState] RevokeFlag called on non-server instance.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(flagId))
        {
            return false;
        }

        FixedString64Bytes fixedFlag = new FixedString64Bytes(flagId);

        for (int i = 0; i < _replicatedFlags.Count; i++)
        {
            if (!_replicatedFlags[i].Equals(fixedFlag))
            {
                continue;
            }

            _replicatedFlags.RemoveAt(i);

            if (verboseLogging)
            {
                Debug.Log($"[PlayerProgressState] Revoked flag '{flagId}'.", this);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Server-only.
    /// Revokes all flags whose ID starts with the given prefix.
    ///
    /// Examples:
    /// - RevokeFlagsByPrefix("intro.")
    /// - RevokeFlagsByPrefix("quest.bandits.")
    /// </summary>
    public int RevokeFlagsByPrefix(string prefix)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerProgressState] RevokeFlagsByPrefix called on non-server instance.", this);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return 0;
        }

        int removed = 0;

        for (int i = _replicatedFlags.Count - 1; i >= 0; i--)
        {
            string flag = _replicatedFlags[i].ToString();

            if (!flag.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            _replicatedFlags.RemoveAt(i);
            removed++;
        }

        if (removed > 0 && verboseLogging)
        {
            Debug.Log($"[PlayerProgressState] Revoked {removed} flag(s) with prefix '{prefix}'.", this);
        }

        return removed;
    }

    // ---------------------------------------------------------------------
    // Persistence API
    // ---------------------------------------------------------------------

    public List<string> CreateFlagSaveData()
    {
        List<string> result = new List<string>();

        for (int i = 0; i < _replicatedFlags.Count; i++)
        {
            string flag = _replicatedFlags[i].ToString();

            if (string.IsNullOrWhiteSpace(flag))
            {
                continue;
            }

            if (result.Contains(flag))
            {
                continue;
            }

            result.Add(flag);
        }

        return result;
    }

    /// <summary>
    /// Server-only save loading.
    /// Replaces current flags with saved flags.
    /// </summary>
    public void ReplaceFlagsFromSaveServer(IEnumerable<string> flags)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerProgressState] ReplaceFlagsFromSaveServer called on non-server instance.", this);
            return;
        }

        _replicatedFlags.Clear();
        _localFlags.Clear();

        if (flags != null)
        {
            foreach (string flagId in flags)
            {
                if (string.IsNullOrWhiteSpace(flagId))
                {
                    continue;
                }

                if (_localFlags.Contains(flagId))
                {
                    continue;
                }

                _localFlags.Add(flagId);
                _replicatedFlags.Add(new FixedString64Bytes(flagId));
            }
        }

        RebuildLocalFlags();
        FlagsChanged?.Invoke();

        if (verboseLogging)
        {
            Debug.Log($"[PlayerProgressState] Loaded {_localFlags.Count} saved flag(s).", this);
        }
    }

    private void HandleFlagsChanged(NetworkListEvent<FixedString64Bytes> changeEvent)
    {
        RebuildLocalFlags();
        FlagsChanged?.Invoke();
    }

    private void RebuildLocalFlags()
    {
        _localFlags.Clear();

        for (int i = 0; i < _replicatedFlags.Count; i++)
        {
            string flag = _replicatedFlags[i].ToString();

            if (!string.IsNullOrWhiteSpace(flag))
            {
                _localFlags.Add(flag);
            }
        }
    }
}