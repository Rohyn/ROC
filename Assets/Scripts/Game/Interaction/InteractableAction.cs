using UnityEngine;

/// <summary>
/// Base class for one modular interaction behavior.
///
/// A GenericInteractable can have multiple actions attached to the same object.
/// They will execute in order.
///
/// Examples later:
/// - rotate a door
/// - apply/remove status
/// - teleport
/// - grant item
/// - trigger trap
/// - start gathering
/// </summary>
public abstract class InteractableAction : MonoBehaviour
{
    [Header("Execution")]
    [Tooltip("Lower numbers execute earlier. Use this when one action must happen before another.")]
    [SerializeField] private int executionOrder = 0;

    public int ExecutionOrder => executionOrder;

    /// <summary>
    /// Returns true if this action is currently allowed to run.
    ///
    /// Examples later:
    /// - requires key
    /// - requires not already seated
    /// - requires skill
    /// - requires available destination
    /// </summary>
    public virtual bool CanExecute(InteractionContext context)
    {
        return true;
    }

    /// <summary>
    /// Performs the action.
    ///
    /// This should assume CanExecute has already passed.
    /// </summary>
    public abstract void Execute(InteractionContext context);
}