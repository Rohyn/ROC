using System;

namespace ROC.Statuses
{
    /// <summary>
    /// Binary gameplay restrictions or capabilities granted by active statuses.
    ///
    /// IMPORTANT:
    /// These are intended for simple yes/no questions such as:
    /// - "Can the player move?"
    /// - "Can the player perform actions?"
    /// - "Should this status be shown in combat UI?"
    ///
    /// These flags are NOT intended for:
    /// - variable magnitudes like "move 30% slower"
    /// - timed damage
    /// - custom procedural behavior
    ///
    /// Those should be represented by modifiers or periodic effects instead.
    /// </summary>
    [Flags]
    public enum StatusFlags
    {
        None = 0,

        // --- Control / action gating ----------------------------------------

        /// <summary>
        /// Prevents movement input from producing movement.
        /// Example uses: Resting, Frozen, Rooted.
        /// </summary>
        NoMovement = 1 << 0,

        /// <summary>
        /// Prevents the player from performing actions.
        /// Example uses: Stunned, Frozen, Silenced-like mechanics if generalized later.
        /// </summary>
        NoActions = 1 << 1,

        /// <summary>
        /// Prevents rotation or turning if you later want a distinct rotation lock.
        /// </summary>
        NoRotation = 1 << 2,

        /// <summary>
        /// Prevents interacting with objects or NPCs.
        /// Useful later if certain statuses should block interaction.
        /// </summary>
        NoInteraction = 1 << 3,

        // --- Display / UI behavior ------------------------------------------

        /// <summary>
        /// Indicates that this status should appear in general UI displays.
        /// Example: the standard buff/debuff bar.
        /// </summary>
        DisplayInGeneralUI = 1 << 8,

        /// <summary>
        /// Indicates that this status is important enough to show in combat-focused UI.
        /// This supports your "combat_display" concept.
        /// </summary>
        DisplayInCombatUI = 1 << 9,

        /// <summary>
        /// Indicates that the status should be hidden from ordinary player-facing UI.
        /// Useful for internal helper statuses or hidden backend state.
        /// </summary>
        HiddenFromUI = 1 << 10,

        // --- Regeneration / state control -----------------------------------

        /// <summary>
        /// Prevents natural regeneration if you later add it.
        /// </summary>
        SuppressNaturalRegen = 1 << 16,

        /// <summary>
        /// Marks the status as something that should generally be considered harmful.
        /// This can later help with UI color-coding, cleansing, AI, or tooltips.
        /// </summary>
        Harmful = 1 << 17,

        /// <summary>
        /// Marks the status as beneficial.
        /// </summary>
        Beneficial = 1 << 18
    }
}