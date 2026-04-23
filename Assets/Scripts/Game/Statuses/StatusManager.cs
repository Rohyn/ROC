using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ROC.Statuses
{
    /// <summary>
    /// Server-authoritative status manager.
    ///
    /// DESIGN:
    /// - The server owns the authoritative mutable status instances.
    /// - Clients receive a read-only replicated snapshot list via NetworkList.
    /// - Clients resolve snapshot StatusIds through a shared StatusCatalog.
    ///
    /// THIS VERSION SUPPORTS:
    /// - server-authoritative apply / remove
    /// - timed expiry on the server
    /// - replicated active statuses for clients
    /// - client-side flag/modifier queries based on the replicated view
    ///
    /// THIS VERSION DOES NOT YET DO:
    /// - periodic damage/heal execution
    /// - special source metadata replication
    /// - prediction / rollback
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class StatusManager : NetworkBehaviour
    {
        [Header("References")]
        [Tooltip("Catalog used to resolve replicated status IDs back into local StatusDefinition assets.")]
        [SerializeField] private StatusCatalog statusCatalog;

        [Header("Debug")]
        [Tooltip("If true, status applications/removals and sync events will be logged.")]
        [SerializeField] private bool verboseLogging = true;

        /// <summary>
        /// Server-only authoritative runtime status instances.
        /// </summary>
        private readonly List<StatusInstance> _authoritativeStatuses = new();

        /// <summary>
        /// Replicated read-only status view shared with clients.
        /// </summary>
        private readonly NetworkList<ReplicatedStatusState> _replicatedStatuses = new();

        /// <summary>
        /// Cached aggregate flags built from the current local view of statuses.
        /// On server: derived from authoritative statuses.
        /// On clients: derived from replicated statuses.
        /// </summary>
        private StatusFlags _combinedFlags = StatusFlags.None;

        /// <summary>
        /// Fired whenever the local visible status state changes.
        /// This fires on server when authoritative state changes and on clients when replicated state changes.
        /// </summary>
        public event Action StatusesChanged;

        public StatusFlags CombinedFlags => _combinedFlags;

        /// <summary>
        /// Server-only authoritative list exposure.
        /// Useful for debugging, but not meaningful on pure clients.
        /// </summary>
        public IReadOnlyList<StatusInstance> ActiveStatuses => _authoritativeStatuses;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                _replicatedStatuses.OnListChanged += HandleReplicatedStatusesChanged;
                RebuildDerivedStateFromReplicated();
                RaiseStatusesChanged();
            }
            else
            {
                RebuildDerivedStateFromAuthoritative();
                PublishReplicatedStatuses();
                RaiseStatusesChanged();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
            {
                _replicatedStatuses.OnListChanged -= HandleReplicatedStatusesChanged;
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            TickStatuses(Time.deltaTime);
        }

        /// <summary>
        /// Applies a status on the authoritative server only.
        ///
        /// Returns true if the status state changed.
        /// </summary>
        public bool ApplyStatus(StatusDefinition definition, int initialStacks = 1, float durationOverrideSeconds = -1f)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[StatusManager] ApplyStatus was called on a non-server instance.", this);
                return false;
            }

            if (definition == null)
            {
                Debug.LogError("[StatusManager] Cannot apply a null StatusDefinition.", this);
                return false;
            }

            StatusInstance existing = FindAuthoritativeInstance(definition);
            float appliedDuration = CalculateAppliedDuration(definition, durationOverrideSeconds);
            int clampedStacks = Mathf.Clamp(initialStacks, 1, Mathf.Max(1, definition.MaxStacks));

            switch (definition.StackingMode)
            {
                case StatusStackingMode.Unique:
                {
                    if (existing != null)
                    {
                        if (verboseLogging)
                        {
                            Debug.Log($"[StatusManager] '{definition.DisplayName}' already present; Unique re-application ignored.", this);
                        }

                        return false;
                    }

                    StatusInstance created = new StatusInstance(definition, clampedStacks, appliedDuration);
                    _authoritativeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Applied unique status '{definition.DisplayName}'.", this);
                    }

                    HandleTransformIfNeeded(created);
                    CommitAuthoritativeChanges();
                    return true;
                }

                case StatusStackingMode.RefreshDuration:
                {
                    if (existing != null)
                    {
                        if (definition.DurationType == StatusDurationType.Timed)
                        {
                            existing.RefreshDuration(appliedDuration);
                        }

                        if (verboseLogging)
                        {
                            Debug.Log($"[StatusManager] Refreshed duration of status '{definition.DisplayName}'.", this);
                        }

                        CommitAuthoritativeChanges();
                        return true;
                    }

                    StatusInstance created = new StatusInstance(definition, clampedStacks, appliedDuration);
                    _authoritativeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Applied status '{definition.DisplayName}' with RefreshDuration behavior.", this);
                    }

                    HandleTransformIfNeeded(created);
                    CommitAuthoritativeChanges();
                    return true;
                }

                case StatusStackingMode.AddStacks:
                {
                    if (existing != null)
                    {
                        existing.AddStacks(clampedStacks, definition.MaxStacks);

                        if (definition.DurationType == StatusDurationType.Timed)
                        {
                            existing.RefreshDuration(appliedDuration);
                        }

                        if (verboseLogging)
                        {
                            Debug.Log($"[StatusManager] Added stacks to status '{definition.DisplayName}'. New stacks: {existing.Stacks}", this);
                        }

                        HandleTransformIfNeeded(existing);
                        CommitAuthoritativeChanges();
                        return true;
                    }

                    StatusInstance created = new StatusInstance(definition, clampedStacks, appliedDuration);
                    _authoritativeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Applied new stacked status '{definition.DisplayName}' with {created.Stacks} stack(s).", this);
                    }

                    HandleTransformIfNeeded(created);
                    CommitAuthoritativeChanges();
                    return true;
                }

                case StatusStackingMode.Replace:
                {
                    if (existing != null)
                    {
                        _authoritativeStatuses.Remove(existing);
                    }

                    StatusInstance created = new StatusInstance(definition, clampedStacks, appliedDuration);
                    _authoritativeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Replaced status '{definition.DisplayName}'.", this);
                    }

                    HandleTransformIfNeeded(created);
                    CommitAuthoritativeChanges();
                    return true;
                }

                default:
                    Debug.LogError($"[StatusManager] Unhandled stacking mode '{definition.StackingMode}' for status '{definition.DisplayName}'.", this);
                    return false;
            }
        }

        /// <summary>
        /// Removes a status on the authoritative server only.
        /// Returns true if something was removed.
        /// </summary>
        public bool RemoveStatus(StatusDefinition definition)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[StatusManager] RemoveStatus was called on a non-server instance.", this);
                return false;
            }

            if (definition == null)
            {
                return false;
            }

            StatusInstance existing = FindAuthoritativeInstance(definition);
            if (existing == null)
            {
                return false;
            }

            _authoritativeStatuses.Remove(existing);

            if (verboseLogging)
            {
                Debug.Log($"[StatusManager] Removed status '{definition.DisplayName}'.", this);
            }

            CommitAuthoritativeChanges();
            return true;
        }

        /// <summary>
        /// Returns true if this entity currently has the specified status.
        /// On server: authoritative view.
        /// On clients: replicated view.
        /// </summary>
        public bool HasStatus(StatusDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            if (IsServer)
            {
                return FindAuthoritativeInstance(definition) != null;
            }

            FixedString64Bytes statusId = new FixedString64Bytes(definition.StatusId);
            for (int i = 0; i < _replicatedStatuses.Count; i++)
            {
                if (_replicatedStatuses[i].StatusId.Equals(statusId))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasFlag(StatusFlags flag)
        {
            return (_combinedFlags & flag) == flag;
        }

        public float GetMultiplicativeModifier(StatusModifierType modifierType)
        {
            if (IsServer)
            {
                return GetMultiplicativeModifierFromAuthoritative(modifierType);
            }

            return GetMultiplicativeModifierFromReplicated(modifierType);
        }

        public float GetAdditiveModifier(StatusModifierType modifierType)
        {
            if (IsServer)
            {
                return GetAdditiveModifierFromAuthoritative(modifierType);
            }

            return GetAdditiveModifierFromReplicated(modifierType);
        }

        /// <summary>
        /// Returns the remaining duration for a status as seen locally.
        /// This is primarily useful for UI later.
        ///
        /// Returns:
        /// - 0 for missing status
        /// - 0 for non-timed statuses
        /// - remaining seconds for timed statuses
        /// </summary>
        public float GetRemainingDurationSeconds(StatusDefinition definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            if (IsServer)
            {
                StatusInstance instance = FindAuthoritativeInstance(definition);
                if (instance == null)
                {
                    return 0f;
                }

                return instance.Definition.DurationType == StatusDurationType.Timed
                    ? Mathf.Max(0f, instance.RemainingDurationSeconds)
                    : 0f;
            }

            FixedString64Bytes statusId = new FixedString64Bytes(definition.StatusId);

            for (int i = 0; i < _replicatedStatuses.Count; i++)
            {
                ReplicatedStatusState state = _replicatedStatuses[i];
                if (!state.StatusId.Equals(statusId))
                {
                    continue;
                }

                if (state.ExpireAtServerTimeSeconds <= 0d || NetworkManager == null)
                {
                    return 0f;
                }

                double remaining = state.ExpireAtServerTimeSeconds - NetworkManager.ServerTime.Time;
                return remaining > 0d ? (float)remaining : 0f;
            }

            return 0f;
        }

        private void TickStatuses(float deltaTime)
        {
            bool changed = false;

            for (int i = _authoritativeStatuses.Count - 1; i >= 0; i--)
            {
                StatusInstance instance = _authoritativeStatuses[i];
                if (instance == null)
                {
                    _authoritativeStatuses.RemoveAt(i);
                    changed = true;
                    continue;
                }

                bool expired = instance.Tick(deltaTime);
                if (!expired)
                {
                    continue;
                }

                if (verboseLogging)
                {
                    Debug.Log($"[StatusManager] Timed status '{instance.Definition.DisplayName}' expired.", this);
                }

                _authoritativeStatuses.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                CommitAuthoritativeChanges();
            }
        }

        private void HandleTransformIfNeeded(StatusInstance instance)
        {
            if (instance == null || instance.Definition == null)
            {
                return;
            }

            StatusTransformationRule rule = instance.Definition.TransformationRule;

            if (!rule.enabled)
            {
                return;
            }

            if (instance.Stacks < rule.requiredStacks)
            {
                return;
            }

            if (rule.targetStatus == null)
            {
                Debug.LogWarning($"[StatusManager] Status '{instance.Definition.DisplayName}' has an enabled transformation rule but no target status assigned.", this);
                return;
            }

            if (rule.targetStatus == instance.Definition)
            {
                Debug.LogWarning($"[StatusManager] Status '{instance.Definition.DisplayName}' cannot transform into itself.", this);
                return;
            }

            if (verboseLogging)
            {
                Debug.Log($"[StatusManager] Status '{instance.Definition.DisplayName}' transformed into '{rule.targetStatus.DisplayName}'.", this);
            }

            if (rule.removeSourceStatus)
            {
                _authoritativeStatuses.Remove(instance);
            }

            ApplyStatus(rule.targetStatus, rule.targetStartingStacks);
        }

        private void CommitAuthoritativeChanges()
        {
            RebuildDerivedStateFromAuthoritative();
            PublishReplicatedStatuses();
            RaiseStatusesChanged();
        }

        private void PublishReplicatedStatuses()
        {
            _replicatedStatuses.Clear();

            double serverTime = NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

            for (int i = 0; i < _authoritativeStatuses.Count; i++)
            {
                StatusInstance instance = _authoritativeStatuses[i];
                if (instance == null || instance.Definition == null)
                {
                    continue;
                }

                double expireAtServerTimeSeconds = 0d;

                if (instance.Definition.DurationType == StatusDurationType.Timed)
                {
                    expireAtServerTimeSeconds = serverTime + Mathf.Max(0f, instance.RemainingDurationSeconds);
                }

                _replicatedStatuses.Add(new ReplicatedStatusState
                {
                    StatusId = new FixedString64Bytes(instance.Definition.StatusId),
                    Stacks = instance.Stacks,
                    ExpireAtServerTimeSeconds = expireAtServerTimeSeconds
                });
            }
        }

        private void HandleReplicatedStatusesChanged(NetworkListEvent<ReplicatedStatusState> changeEvent)
        {
            RebuildDerivedStateFromReplicated();
            RaiseStatusesChanged();
        }

        private void RebuildDerivedStateFromAuthoritative()
        {
            _combinedFlags = StatusFlags.None;

            for (int i = 0; i < _authoritativeStatuses.Count; i++)
            {
                StatusInstance instance = _authoritativeStatuses[i];
                if (instance?.Definition == null)
                {
                    continue;
                }

                _combinedFlags |= instance.Definition.Flags;
            }
        }

        private void RebuildDerivedStateFromReplicated()
        {
            _combinedFlags = StatusFlags.None;

            if (statusCatalog == null)
            {
                return;
            }

            for (int i = 0; i < _replicatedStatuses.Count; i++)
            {
                ReplicatedStatusState state = _replicatedStatuses[i];

                if (!statusCatalog.TryGetDefinition(state.StatusId.ToString(), out StatusDefinition definition) || definition == null)
                {
                    continue;
                }

                _combinedFlags |= definition.Flags;
            }
        }

        private StatusInstance FindAuthoritativeInstance(StatusDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            for (int i = 0; i < _authoritativeStatuses.Count; i++)
            {
                StatusInstance instance = _authoritativeStatuses[i];
                if (instance != null && instance.Definition == definition)
                {
                    return instance;
                }
            }

            return null;
        }

        private float GetMultiplicativeModifierFromAuthoritative(StatusModifierType modifierType)
        {
            float result = 1f;

            for (int i = 0; i < _authoritativeStatuses.Count; i++)
            {
                StatusInstance instance = _authoritativeStatuses[i];
                if (instance?.Definition == null)
                {
                    continue;
                }

                StatusModifierDefinition[] modifiers = instance.Definition.Modifiers;
                if (modifiers == null)
                {
                    continue;
                }

                for (int j = 0; j < modifiers.Length; j++)
                {
                    StatusModifierDefinition modifier = modifiers[j];
                    if (modifier.modifierType == modifierType &&
                        modifier.operation == StatusModifierOperation.Multiply)
                    {
                        result *= modifier.value;
                    }
                }
            }

            return result;
        }

        private float GetAdditiveModifierFromAuthoritative(StatusModifierType modifierType)
        {
            float result = 0f;

            for (int i = 0; i < _authoritativeStatuses.Count; i++)
            {
                StatusInstance instance = _authoritativeStatuses[i];
                if (instance?.Definition == null)
                {
                    continue;
                }

                StatusModifierDefinition[] modifiers = instance.Definition.Modifiers;
                if (modifiers == null)
                {
                    continue;
                }

                for (int j = 0; j < modifiers.Length; j++)
                {
                    StatusModifierDefinition modifier = modifiers[j];
                    if (modifier.modifierType == modifierType &&
                        modifier.operation == StatusModifierOperation.Add)
                    {
                        result += modifier.value;
                    }
                }
            }

            return result;
        }

        private float GetMultiplicativeModifierFromReplicated(StatusModifierType modifierType)
        {
            float result = 1f;

            if (statusCatalog == null)
            {
                return result;
            }

            for (int i = 0; i < _replicatedStatuses.Count; i++)
            {
                ReplicatedStatusState state = _replicatedStatuses[i];

                if (!statusCatalog.TryGetDefinition(state.StatusId.ToString(), out StatusDefinition definition) || definition == null)
                {
                    continue;
                }

                StatusModifierDefinition[] modifiers = definition.Modifiers;
                if (modifiers == null)
                {
                    continue;
                }

                for (int j = 0; j < modifiers.Length; j++)
                {
                    StatusModifierDefinition modifier = modifiers[j];
                    if (modifier.modifierType == modifierType &&
                        modifier.operation == StatusModifierOperation.Multiply)
                    {
                        result *= modifier.value;
                    }
                }
            }

            return result;
        }

        private float GetAdditiveModifierFromReplicated(StatusModifierType modifierType)
        {
            float result = 0f;

            if (statusCatalog == null)
            {
                return result;
            }

            for (int i = 0; i < _replicatedStatuses.Count; i++)
            {
                ReplicatedStatusState state = _replicatedStatuses[i];

                if (!statusCatalog.TryGetDefinition(state.StatusId.ToString(), out StatusDefinition definition) || definition == null)
                {
                    continue;
                }

                StatusModifierDefinition[] modifiers = definition.Modifiers;
                if (modifiers == null)
                {
                    continue;
                }

                for (int j = 0; j < modifiers.Length; j++)
                {
                    StatusModifierDefinition modifier = modifiers[j];
                    if (modifier.modifierType == modifierType &&
                        modifier.operation == StatusModifierOperation.Add)
                    {
                        result += modifier.value;
                    }
                }
            }

            return result;
        }

        private float CalculateAppliedDuration(StatusDefinition definition, float durationOverrideSeconds)
        {
            if (definition == null)
            {
                return 0f;
            }

            if (definition.DurationType != StatusDurationType.Timed)
            {
                return 0f;
            }

            if (durationOverrideSeconds >= 0f)
            {
                return durationOverrideSeconds;
            }

            return definition.DefaultDurationSeconds;
        }

        private void RaiseStatusesChanged()
        {
            StatusesChanged?.Invoke();
        }
    }
}