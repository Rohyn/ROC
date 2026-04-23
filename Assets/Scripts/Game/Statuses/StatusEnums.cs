namespace ROC.Statuses
{
    /// <summary>
    /// Describes how a status determines its duration.
    /// </summary>
    public enum StatusDurationType
    {
        /// <summary>
        /// The status lasts forever until explicitly removed.
        /// Example: a stance, a passive aura, or some backend state.
        /// </summary>
        Permanent = 0,

        /// <summary>
        /// The status has a default authored duration in seconds.
        /// Example: stun for 2 seconds, poison for 10 seconds.
        /// </summary>
        Timed = 1,

        /// <summary>
        /// The status has no meaningful duration by itself and must be removed
        /// by some condition or gameplay action.
        /// Example: Resting until the player stands up.
        /// </summary>
        Conditional = 2
    }

    /// <summary>
    /// Describes how repeated applications of the same status behave.
    /// </summary>
    public enum StatusStackingMode
    {
        /// <summary>
        /// Re-applying the status does nothing if it is already present.
        /// </summary>
        Unique = 0,

        /// <summary>
        /// Re-applying the status refreshes its duration but does not increase stacks.
        /// </summary>
        RefreshDuration = 1,

        /// <summary>
        /// Re-applying the status adds stacks up to a configured maximum.
        /// </summary>
        AddStacks = 2,

        /// <summary>
        /// Re-applying the status replaces the existing instance entirely.
        /// This is less common, but it can be useful in some designs.
        /// </summary>
        Replace = 3
    }

    /// <summary>
    /// Defines how a numeric modifier should combine with others of the same type.
    /// </summary>
    public enum StatusModifierOperation
    {
        /// <summary>
        /// Adds a flat amount.
        /// Example: +10 max health.
        /// </summary>
        Add = 0,

        /// <summary>
        /// Multiplies by a factor.
        /// Example: 0.7 move speed, 1.25 attack speed.
        /// </summary>
        Multiply = 1
    }

    /// <summary>
    /// What kind of stat or derived value a modifier affects.
    /// Keep this list small at first; expand only when needed.
    /// </summary>
    public enum StatusModifierType
    {
        MoveSpeed = 0,
        ActionSpeed = 1,
        DamageTaken = 2,
        HealingReceived = 3,
        MaxHealth = 4
    }

    /// <summary>
    /// What kind of periodic effect the status produces over time.
    /// This is just authored data for now; runtime execution comes later.
    /// </summary>
    public enum PeriodicEffectType
    {
        Damage = 0,
        Heal = 1
    }
}