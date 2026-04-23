using UnityEngine;

namespace ROC.Statuses
{
    /// <summary>
    /// Authored definition for a gameplay status.
    ///
    /// IMPORTANT:
    /// This ScriptableObject is a shared data asset.
    /// It should NOT hold per-player runtime state such as:
    /// - remaining duration
    /// - current stacks
    /// - source entity
    /// - attached furniture
    ///
    /// That runtime state will live later in a StatusInstance.
    ///
    /// This asset defines what a status IS:
    /// - how it is displayed
    /// - what flags it grants
    /// - how it stacks
    /// - what modifiers it contributes
    /// - whether it has periodic effects
    /// - whether it transforms into another status at some threshold
    /// </summary>
    [CreateAssetMenu(
        fileName = "StatusDefinition",
        menuName = "ROC/Statuses/Status Definition")]
    public class StatusDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable internal ID for this status. Use lowercase_with_underscores for consistency.")]
        [SerializeField] private string statusId = "new_status";

        [Tooltip("Player-facing display name.")]
        [SerializeField] private string displayName = "New Status";

        [TextArea]
        [Tooltip("Optional design/dev description of the status.")]
        [SerializeField] private string description;

        [Header("Display")]
        [Tooltip("Optional icon for UI use later.")]
        [SerializeField] private Sprite icon;

        [Tooltip("Binary flags controlling restrictions and display behavior.")]
        [SerializeField] private StatusFlags flags =
            StatusFlags.DisplayInGeneralUI;

        [Header("Duration")]
        [Tooltip("How this status determines its lifetime.")]
        [SerializeField] private StatusDurationType durationType = StatusDurationType.Timed;

        [Tooltip("Default duration in seconds if Duration Type is Timed.")]
        [Min(0f)]
        [SerializeField] private float defaultDurationSeconds = 5f;

        [Header("Stacking")]
        [Tooltip("How repeated applications of this status behave.")]
        [SerializeField] private StatusStackingMode stackingMode = StatusStackingMode.Unique;

        [Tooltip("Maximum stacks allowed when stacking mode supports stacks.")]
        [Min(1)]
        [SerializeField] private int maxStacks = 1;

        [Header("Modifiers")]
        [Tooltip("Numeric stat or derived-value modifiers contributed by this status.")]
        [SerializeField] private StatusModifierDefinition[] modifiers;

        [Header("Periodic Effects")]
        [Tooltip("Periodic effects such as damage-over-time or healing-over-time.")]
        [SerializeField] private PeriodicEffectDefinition[] periodicEffects;

        [Header("Transformation")]
        [Tooltip("Optional rule for transforming this status into another at a stack threshold.")]
        [SerializeField] private StatusTransformationRule transformationRule;

        // --------------------------------------------------------------------
        // Public read-only accessors
        // --------------------------------------------------------------------

        public string StatusId => statusId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public StatusFlags Flags => flags;
        public StatusDurationType DurationType => durationType;
        public float DefaultDurationSeconds => defaultDurationSeconds;
        public StatusStackingMode StackingMode => stackingMode;
        public int MaxStacks => maxStacks;
        public StatusModifierDefinition[] Modifiers => modifiers;
        public PeriodicEffectDefinition[] PeriodicEffects => periodicEffects;
        public StatusTransformationRule TransformationRule => transformationRule;

        /// <summary>
        /// Convenience helper for flag checks.
        /// This lets other code ask:
        /// definition.HasFlag(StatusFlags.NoMovement)
        /// instead of manually masking every time.
        /// </summary>
        public bool HasFlag(StatusFlags flag)
        {
            return (flags & flag) == flag;
        }

        private void OnValidate()
        {
            // Keep the asset data in a sane state when edited in the Inspector.

            if (string.IsNullOrWhiteSpace(statusId))
            {
                statusId = "new_status";
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }

            if (maxStacks < 1)
            {
                maxStacks = 1;
            }

            if (durationType != StatusDurationType.Timed)
            {
                // For non-timed statuses, duration is not meaningful.
                // We leave it at 0 to avoid implying otherwise.
                defaultDurationSeconds = 0f;
            }

            if (durationType == StatusDurationType.Timed && defaultDurationSeconds < 0f)
            {
                defaultDurationSeconds = 0f;
            }

            // If the stacking mode is Unique / RefreshDuration / Replace,
            // there is no meaningful stack count above 1.
            if (stackingMode != StatusStackingMode.AddStacks)
            {
                maxStacks = 1;
            }

            // If the transformation rule is enabled, keep its values sane.
            if (transformationRule.enabled)
            {
                if (transformationRule.requiredStacks < 1)
                {
                    transformationRule.requiredStacks = 1;
                }

                if (transformationRule.targetStartingStacks < 1)
                {
                    transformationRule.targetStartingStacks = 1;
                }
            }
        }
    }
}