using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maintains a live set of nearby interactables using a throttled OverlapSphere query.
///
/// WHY THIS VERSION EXISTS:
/// - Trigger-based sensing is awkward with a CharacterController-driven player unless
///   the collider setup also includes a Rigidbody somewhere in the interaction pair.
/// - This polling approach is still efficient because it runs on a light cadence and
///   only searches a small radius around the player.
/// - It preserves the "small nearby candidate set" architecture you wanted.
///
/// Attach this directly to the player root.
/// It does NOT require a trigger collider child object.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractionSensor : MonoBehaviour
{
    [Header("Search")]
    [Tooltip("How far from the player to search for nearby interactables.")]
    [SerializeField] private float searchRadius = 2.5f;

    [Tooltip("Vertical offset from the player root used as the center of the search sphere.")]
    [SerializeField] private float searchOriginHeight = 1.0f;

    [Tooltip("How often the nearby interactable set should be rebuilt, in seconds.")]
    [SerializeField] private float refreshIntervalSeconds = 0.08f;

    [Tooltip("Layer mask used to search for possible interactable colliders.")]
    [SerializeField] private LayerMask interactableMask = ~0;

    [Tooltip("Whether trigger colliders should be included in the search.")]
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Debug")]
    [Tooltip("If true, nearby candidate changes will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    /// <summary>
    /// Current nearby interactables.
    /// </summary>
    private readonly HashSet<GenericInteractable> _nearbyInteractables = new();

    /// <summary>
    /// Temporary set used while rebuilding the nearby list.
    /// </summary>
    private readonly HashSet<GenericInteractable> _rebuildBuffer = new();

    /// <summary>
    /// Reusable physics buffer to avoid allocations.
    /// </summary>
    private readonly Collider[] _overlapResults = new Collider[32];

    private float _nextRefreshTime;

    /// <summary>
    /// Public read-only access to the current nearby interactable set.
    /// </summary>
    public IReadOnlyCollection<GenericInteractable> NearbyInteractables => _nearbyInteractables;

    /// <summary>
    /// Raised whenever the nearby candidate set changes.
    /// The selector can listen to this to refresh immediately.
    /// </summary>
    public event Action NearbySetChanged;

    private void Update()
    {
        if (Time.time >= _nextRefreshTime)
        {
            RefreshNearbyInteractables();
            _nextRefreshTime = Time.time + refreshIntervalSeconds;
        }
    }

    /// <summary>
    /// Rebuilds the nearby interactable set using OverlapSphereNonAlloc.
    /// </summary>
    public void RefreshNearbyInteractables()
    {
        Vector3 origin = transform.position + Vector3.up * searchOriginHeight;

        int hitCount = Physics.OverlapSphereNonAlloc(
            origin,
            searchRadius,
            _overlapResults,
            interactableMask,
            triggerInteraction);

        _rebuildBuffer.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _overlapResults[i];
            if (hit == null)
            {
                continue;
            }

            GenericInteractable interactable = hit.GetComponentInParent<GenericInteractable>();
            if (interactable == null)
            {
                continue;
            }

            _rebuildBuffer.Add(interactable);
        }

        bool changed = false;

        // Remove things no longer present.
        List<GenericInteractable> toRemove = null;
        foreach (GenericInteractable interactable in _nearbyInteractables)
        {
            if (_rebuildBuffer.Contains(interactable))
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
                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInteractionSensor] Removed nearby interactable '{toRemove[i]?.name}'.", this);
                }

                _nearbyInteractables.Remove(toRemove[i]);
                changed = true;
            }
        }

        // Add newly present things.
        foreach (GenericInteractable interactable in _rebuildBuffer)
        {
            if (_nearbyInteractables.Add(interactable))
            {
                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInteractionSensor] Added nearby interactable '{interactable.name}'.", this);
                }

                changed = true;
            }
        }

        if (changed)
        {
            NearbySetChanged?.Invoke();
        }
    }

    /// <summary>
    /// Removes null / destroyed references from the nearby set.
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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 origin = transform.position + Vector3.up * searchOriginHeight;
        Gizmos.DrawWireSphere(origin, searchRadius);
    }
}