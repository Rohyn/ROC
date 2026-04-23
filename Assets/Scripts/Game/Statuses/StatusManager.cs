using System;
using System.Collections.Generic;
using UnityEngine;

namespace ROC.Statuses
{
    /// <summary>
    /// Holds and manages all active runtime statuses on an entity.
    ///
    /// RESPONSIBILITIES:
    /// - add statuses
    /// - remove statuses
    /// - tick timed statuses
    /// - answer aggregate questions like:
    ///   - "Does this player have NoMovement?"
    ///   - "What is the move speed multiplier?"
    /// - apply authored transformation rules such as Chill -> Frozen
    ///
    /// IMPORTANT:
    /// This first version is local/runtime only.
    /// It is NOT yet replicated across the network.
    /// That is okay for now because the goal is to validate structure and behavior.
    /// </summary>
    [DisallowMultipleComponent]
    public class StatusManager : MonoBehaviour
    {
        [Header("Debug")]
        [Tooltip("If true, status applications/removals and other important events will be logged.")]
        [SerializeField] private bool verboseLogging = true;

        /// <summary>
        /// Current active runtime statuses on this entity.
        /// </summary>
        private readonly List<StatusInstance> _activeStatuses = new();

        /// <summary>
        /// Cached aggregate flags built from all active statuses.
        /// This is useful because flags are checked often.
        /// </summary>
        private StatusFlags _combinedFlags = StatusFlags.None;

        /// <summary>
        /// Fired whenever the active status set changes in a meaningful way.
        /// UI or other systems can subscribe later.
        /// </summary>
        public event Action StatusesChanged;

        /// <summary>
        /// Public read-only access to current combined flags.
        /// </summary>
        public StatusFlags CombinedFlags => _combinedFlags;

        /// <summary>
        /// Public read-only access to the current active statuses.
        /// </summary>
        public IReadOnlyList<StatusInstance> ActiveStatuses => _activeStatuses;

        private void Update()
        {
            TickStatuses(Time.deltaTime);
        }

        /// <summary>
        /// Applies a status to this entity.
        ///
        /// This method obeys the authored stacking rules on the definition:
        /// - Unique
        /// - RefreshDuration
        /// - AddStacks
        /// - Replace
        ///
        /// Returns true if the status state changed.
        /// </summary>
        public bool ApplyStatus(StatusDefinition definition, int initialStacks = 1, float durationOverrideSeconds = -1f)
        {
            if (definition == null)
            {
                Debug.LogError("[StatusManager] Cannot apply a null StatusDefinition.");
                return false;
            }

            StatusInstance existing = FindInstance(definition);

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
                    _activeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Applied unique status '{definition.DisplayName}'.", this);
                    }

                    HandleTransformIfNeeded(created);
                    RebuildDerivedState();
                    RaiseStatusesChanged();
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

                        RebuildDerivedState();
                        RaiseStatusesChanged();
                        return true;
                    }

                    StatusInstance created = new StatusInstance(definition, clampedStacks, appliedDuration);
                    _activeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Applied status '{definition.DisplayName}' with RefreshDuration behavior.", this);
                    }

                    HandleTransformIfNeeded(created);
                    RebuildDerivedState();
                    RaiseStatusesChanged();
                    return true;
                }

                case StatusStackingMode.AddStacks:
                {
                    if (existing != null)
                    {
                        existing.AddStacks(clampedStacks, definition.MaxStacks);

                        // Common MMO behavior:
                        // when a timed stacked status is re-applied, its duration is refreshed.
                        if (definition.DurationType == StatusDurationType.Timed)
                        {
                            existing.RefreshDuration(appliedDuration);
                        }

                        if (verboseLogging)
                        {
                            Debug.Log($"[StatusManager] Added stacks to status '{definition.DisplayName}'. New stacks: {existing.Stacks}", this);
                        }

                        HandleTransformIfNeeded(existing);
                        RebuildDerivedState();
                        RaiseStatusesChanged();
                        return true;
                    }

                    StatusInstance created = new StatusInstance(definition, clampedStacks, appliedDuration);
                    _activeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Applied new stacked status '{definition.DisplayName}' with {created.Stacks} stack(s).", this);
                    }

                    HandleTransformIfNeeded(created);
                    RebuildDerivedState();
                    RaiseStatusesChanged();
                    return true;
                }

                case StatusStackingMode.Replace:
                {
                    if (existing != null)
                    {
                        _activeStatuses.Remove(existing);
                    }

                    StatusInstance created = new StatusInstance(definition, clampedStacks, appliedDuration);
                    _activeStatuses.Add(created);

                    if (verboseLogging)
                    {
                        Debug.Log($"[StatusManager] Replaced status '{definition.DisplayName}'.", this);
                    }

                    HandleTransformIfNeeded(created);
                    RebuildDerivedState();
                    RaiseStatusesChanged();
                    return true;
                }

                default:
                    Debug.LogError($"[StatusManager] Unhandled stacking mode '{definition.StackingMode}' for status '{definition.DisplayName}'.", this);
                    return false;
            }
        }

        /// <summary>
        /// Removes the first matching instance of a status definition.
        /// Returns true if something was removed.
        /// </summary>
        public bool RemoveStatus(StatusDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            StatusInstance existing = FindInstance(definition);
            if (existing == null)
            {
                return false;
            }

            _activeStatuses.Remove(existing);

            if (verboseLogging)
            {
                Debug.Log($"[StatusManager] Removed status '{definition.DisplayName}'.", this);
            }

            RebuildDerivedState();
            RaiseStatusesChanged();
            return true;
        }

        /// <summary>
        /// Returns true if this entity currently has the specified status definition active.
        /// </summary>
        public bool HasStatus(StatusDefinition definition)
        {
            return FindInstance(definition) != null;
        }

        /// <summary>
        /// Returns true if the aggregate status state contains the requested flag.
        ///
        /// Example:
        /// - HasFlag(StatusFlags.NoMovement)
        /// - HasFlag(StatusFlags.NoActions)
        /// </summary>
        public bool HasFlag(StatusFlags flag)
        {
            return (_combinedFlags & flag) == flag;
        }

        /// <summary>
        /// Returns the total multiplicative modifier for the requested modifier type.
        ///
        /// Example:
        /// - if two statuses contribute MoveSpeed x 0.9 and MoveSpeed x 0.8,
        ///   this returns 0.72.
        ///
        /// Default return value is 1.0, meaning "no change".
        /// </summary>
        public float GetMultiplicativeModifier(StatusModifierType modifierType)
        {
            float result = 1f;

            for (int i = 0; i < _activeStatuses.Count; i++)
            {
                StatusInstance instance = _activeStatuses[i];
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

                    if (modifier.modifierType != modifierType)
                    {
                        continue;
                    }

                    if (modifier.operation == StatusModifierOperation.Multiply)
                    {
                        result *= modifier.value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the total additive modifier for the requested modifier type.
        ///
        /// Default return value is 0.0, meaning "no change".
        /// </summary>
        public float GetAdditiveModifier(StatusModifierType modifierType)
        {
            float result = 0f;

            for (int i = 0; i < _activeStatuses.Count; i++)
            {
                StatusInstance instance = _activeStatuses[i];
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

                    if (modifier.modifierType != modifierType)
                    {
                        continue;
                    }

                    if (modifier.operation == StatusModifierOperation.Add)
                    {
                        result += modifier.value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Ticks all timed statuses and removes any that expire.
        ///
        /// IMPORTANT:
        /// This first version does NOT yet execute periodic damage/heal effects.
        /// It only handles timed expiration.
        /// </summary>
        private void TickStatuses(float deltaTime)
        {
            bool changed = false;

            for (int i = _activeStatuses.Count - 1; i >= 0; i--)
            {
                StatusInstance instance = _activeStatuses[i];
                if (instance == null)
                {
                    _activeStatuses.RemoveAt(i);
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

                _activeStatuses.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                RebuildDerivedState();
                RaiseStatusesChanged();
            }
        }

        /// <summary>
        /// Handles transformation rules like Chill -> Frozen when a stack threshold is reached.
        ///
        /// This is intentionally simple:
        /// - if the rule is enabled
        /// - if required stacks are reached
        /// - if a target status exists
        /// then apply the target status and optionally remove the source.
        /// </summary>
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
                _activeStatuses.Remove(instance);
            }

            // Apply the target status.
            // This intentionally goes through ApplyStatus so the target's own stacking rules
            // and duration rules are respected.
            ApplyStatus(rule.targetStatus, rule.targetStartingStacks);
        }

        /// <summary>
        /// Rebuilds cached aggregate state derived from the active status list.
        ///
        /// Right now, this only caches flags because flags are checked often and are cheap to cache.
        /// Numeric modifiers are still queried by scanning the active list.
        /// </summary>
        private void RebuildDerivedState()
        {
            _combinedFlags = StatusFlags.None;

            for (int i = 0; i < _activeStatuses.Count; i++)
            {
                StatusInstance instance = _activeStatuses[i];
                if (instance?.Definition == null)
                {
                    continue;
                }

                _combinedFlags |= instance.Definition.Flags;
            }
        }

        /// <summary>
        /// Finds the first active instance using the given definition.
        /// </summary>
        private StatusInstance FindInstance(StatusDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            for (int i = 0; i < _activeStatuses.Count; i++)
            {
                StatusInstance instance = _activeStatuses[i];
                if (instance != null && instance.Definition == definition)
                {
                    return instance;
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates the duration that should be applied when this status is created or refreshed.
        /// </summary>
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

        /// <summary>
        /// Central helper for raising the changed event.
        /// </summary>
        private void RaiseStatusesChanged()
        {
            StatusesChanged?.Invoke();
        }
    }
}