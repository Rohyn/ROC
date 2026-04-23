/// <summary>
/// Controls who loses access to an interactable after it is successfully used.
///
/// IMPORTANT:
/// This is about AVAILABILITY / CONSUMPTION, not about who receives the effect.
///
/// Examples:
/// - PerPlayer: a key pickup disappears only for the player who picked it up
/// - Global: a one-time world switch or shared relic is no longer available to anyone
/// - None: a reusable bed/chair/door remains available after use
/// </summary>
public enum InteractionConsumptionMode
{
    None = 0,
    PerPlayer = 1,
    Global = 2
}