using System;
using UnityEngine;

namespace ROC.Statuses
{
    /// <summary>
    /// A single numeric modifier contributed by a status.
    ///
    /// Examples:
    /// - MoveSpeed x 0.70 for Frozen
    /// - ActionSpeed x 0.50 for Drunk
    /// - MaxHealth + 10 for a blessing effect
    /// </summary>
    [Serializable]
    public struct StatusModifierDefinition
    {
        [Tooltip("What this modifier affects.")]
        public StatusModifierType modifierType;

        [Tooltip("How the value should combine with the final stat.")]
        public StatusModifierOperation operation;

        [Tooltip("The value of the modifier. Interpretation depends on operation.")]
        public float value;
    }

    /// <summary>
    /// A periodic effect such as damage-over-time or healing-over-time.
    ///
    /// This is authored data only for now.
    /// Runtime ticking/execution will be implemented later.
    /// </summary>
    [Serializable]
    public struct PeriodicEffectDefinition
    {
        [Tooltip("What kind of periodic effect this is.")]
        public PeriodicEffectType effectType;

        [Tooltip("How often the effect should trigger, in seconds.")]
        [Min(0.01f)]
        public float tickIntervalSeconds;

        [Tooltip("The amount applied on each tick.")]
        public float amount;
    }

    /// <summary>
    /// Optional transformation rule used for escalation behavior.
    ///
    /// Example:
    /// - Chill transforms into Frozen at 5 stacks.
    ///
    /// This is authored data only for now.
    /// Runtime application logic will be added later in the status manager.
    /// </summary>
    [Serializable]
    public struct StatusTransformationRule
    {
        [Tooltip("If true, this rule is active.")]
        public bool enabled;

        [Tooltip("How many stacks are required before the status transforms.")]
        [Min(1)]
        public int requiredStacks;

        [Tooltip("The status definition this status should transform into.")]
        public StatusDefinition targetStatus;

        [Tooltip("If true, the old status is removed when the new one is applied.")]
        public bool removeSourceStatus;

        [Tooltip("How many stacks the transformed status should begin with.")]
        [Min(1)]
        public int targetStartingStacks;
    }
}