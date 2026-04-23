using UnityEngine;

namespace ROC.Statuses
{
    /// <summary>
    /// Runtime instance of a status applied to a specific entity.
    ///
    /// IMPORTANT:
    /// This is NOT a ScriptableObject asset.
    /// This is per-entity runtime state.
    ///
    /// The ScriptableObject tells us what a status IS.
    /// This instance tracks how that authored status currently exists on one player:
    /// - remaining duration
    /// - current stacks
    /// - optional context later
    /// </summary>
    public sealed class StatusInstance
    {
        /// <summary>
        /// The authored definition asset that describes this status.
        /// </summary>
        public StatusDefinition Definition { get; }

        /// <summary>
        /// Current number of stacks on this instance.
        /// </summary>
        public int Stacks { get; private set; }

        /// <summary>
        /// Remaining duration in seconds.
        ///
        /// For non-timed statuses, this will typically stay at 0.
        /// </summary>
        public float RemainingDurationSeconds { get; private set; }

        /// <summary>
        /// Convenience access to the status ID from the definition.
        /// </summary>
        public string StatusId => Definition != null ? Definition.StatusId : string.Empty;

        /// <summary>
        /// Returns true if this status should be considered expired and removable.
        /// Only timed statuses can expire this way.
        /// </summary>
        public bool IsExpired =>
            Definition != null &&
            Definition.DurationType == StatusDurationType.Timed &&
            RemainingDurationSeconds <= 0f;

        /// <summary>
        /// Creates a new runtime status instance.
        /// </summary>
        public StatusInstance(StatusDefinition definition, int initialStacks, float initialDurationSeconds)
        {
            Definition = definition;
            Stacks = Mathf.Max(1, initialStacks);
            RemainingDurationSeconds = Mathf.Max(0f, initialDurationSeconds);
        }

        /// <summary>
        /// Adds stacks, up to a specified maximum.
        /// </summary>
        public void AddStacks(int amount, int maxStacks)
        {
            if (amount <= 0)
            {
                return;
            }

            Stacks = Mathf.Clamp(Stacks + amount, 1, Mathf.Max(1, maxStacks));
        }

        /// <summary>
        /// Sets the stack count directly, clamped to a valid range.
        /// </summary>
        public void SetStacks(int newStacks, int maxStacks)
        {
            Stacks = Mathf.Clamp(newStacks, 1, Mathf.Max(1, maxStacks));
        }

        /// <summary>
        /// Refreshes the remaining duration.
        /// Intended for timed statuses when they are re-applied.
        /// </summary>
        public void RefreshDuration(float newDurationSeconds)
        {
            RemainingDurationSeconds = Mathf.Max(0f, newDurationSeconds);
        }

        /// <summary>
        /// Advances the timer for timed statuses.
        ///
        /// Returns true if the instance became expired during this tick.
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (Definition == null)
            {
                return false;
            }

            if (Definition.DurationType != StatusDurationType.Timed)
            {
                return false;
            }

            if (RemainingDurationSeconds <= 0f)
            {
                return true;
            }

            RemainingDurationSeconds -= deltaTime;

            if (RemainingDurationSeconds < 0f)
            {
                RemainingDurationSeconds = 0f;
            }

            return RemainingDurationSeconds <= 0f;
        }
    }
}