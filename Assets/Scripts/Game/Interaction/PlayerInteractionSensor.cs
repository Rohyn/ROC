using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maintains a live set of nearby interactables using trigger enter/exit.
///
/// Attach this to a child trigger collider on the player.
/// Recommended setup:
/// - child object named "InteractionSensor"
/// - SphereCollider with Is Trigger enabled
///
/// This script does NOT decide which candidate is best.
/// It only tracks which interactables are currently nearby enough to matter.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractionSensor : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("If true, nearby candidate changes will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    /// <summary>
    /// Current nearby interactables.
    /// </summary>
    private readonly HashSet<GenericInteractable> _nearbyInteractables = new();

    /// <summary>
    /// Public read-only access to the current nearby interactable set.
    /// </summary>
    public IReadOnlyCollection<GenericInteractable> NearbyInteractables => _nearbyInteractables;

    /// <summary>
    /// Raised whenever the nearby candidate set changes.
    /// The selector can listen to this if it wants to refresh immediately.
    /// </summary>
    public event System.Action NearbySetChanged;

    private void OnTriggerEnter(Collider other)
    {
        GenericInteractable interactable = other.GetComponentInParent<GenericInteractable>();
        if (interactable == null)
        {
            return;
        }

        if (_nearbyInteractables.Add(interactable))
        {
            if (verboseLogging)
            {
                Debug.Log($"[PlayerInteractionSensor] Added nearby interactable '{interactable.name}'.", this);
            }

            NearbySetChanged?.Invoke();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        GenericInteractable interactable = other.GetComponentInParent<GenericInteractable>();
        if (interactable == null)
        {
            return;
        }

        if (_nearbyInteractables.Remove(interactable))
        {
            if (verboseLogging)
            {
                Debug.Log($"[PlayerInteractionSensor] Removed nearby interactable '{interactable.name}'.", this);
            }

            NearbySetChanged?.Invoke();
        }
    }

    /// <summary>
    /// Removes null / destroyed references from the nearby set.
    /// This can happen if an interactable is destroyed while still inside the trigger.
    /// </summary>
    public void CleanupNulls()
    {
        bool changed = false;

        List<GenericInteractable> toRemove = null;

        foreach (GenericInteractable interactable in _nearbyInteractables)
        {
            if (interactable != null)
            {
                continue;
            }

            toRemove ??= new List<GenericInteractable>();
            toRemove.Add(interactable);
        }

        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++)
            {
                _nearbyInteractables.Remove(toRemove[i]);
                changed = true;
            }
        }

        if (changed)
        {
            NearbySetChanged?.Invoke();
        }
    }
}