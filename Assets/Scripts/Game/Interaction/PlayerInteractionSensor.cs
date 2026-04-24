using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Maintains the local player's current set of nearby interactables.
///
/// DESIGN:
/// - Owner-only
/// - Rebuilds the nearby interactable set from fresh overlap results
/// - Robust against scene unloads and destroyed interactables
/// - Clears itself during area transfers so stale references from the old scene
///   do not survive into the new area
///
/// This component intentionally does NOT decide which interactable is best.
/// That is the selector's job.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractionSensor : NetworkBehaviour
{
    [Header("Detection")]
    [SerializeField] private float detectionRadius = 5f;

    [Tooltip("Physics layers to search for interactable colliders on.")]
    [SerializeField] private LayerMask detectionMask = ~0;

    [Tooltip("How often to rebuild the nearby interactable set.")]
    [SerializeField] private float refreshIntervalSeconds = 0.05f;

    [Tooltip("Whether trigger colliders should be considered during overlap checks.")]
    [SerializeField] private QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly HashSet<GenericInteractable> _nearbyInteractables = new();
    private readonly HashSet<GenericInteractable> _currentFrameInteractables = new();

    private float _nextRefreshTime;

    public event Action NearbySetChanged;

    public IReadOnlyCollection<GenericInteractable> NearbyInteractables => _nearbyInteractables;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _nextRefreshTime = 0f;
    }

    private void OnDisable()
    {
        if (_nearbyInteractables.Count > 0)
        {
            _nearbyInteractables.Clear();
            NearbySetChanged?.Invoke();
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (Time.time < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.time + refreshIntervalSeconds;
        RefreshNearbyInteractables();
    }

    /// <summary>
    /// Removes destroyed/null interactables from the cached set.
    /// Safe to call from the selector before scoring.
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

    /// <summary>
    /// Rebuilds the nearby interactable set from a fresh overlap query.
    /// This is intentionally resilient to scene unloads and destroyed objects.
    /// </summary>
    public void RefreshNearbyInteractables()
    {
        CleanupNulls();

        PlayerAreaStreamingController areaStreaming = GetComponent<PlayerAreaStreamingController>();
        if (areaStreaming != null && areaStreaming.IsAreaTransferInProgress)
        {
            if (_nearbyInteractables.Count > 0)
            {
                if (verboseLogging)
                {
                    foreach (GenericInteractable interactable in _nearbyInteractables)
                    {
                        if (interactable != null)
                        {
                            Debug.Log($"[PlayerInteractionSensor] Removed nearby interactable '{GetSafeInteractableName(interactable)}'.");
                        }
                    }
                }

                _nearbyInteractables.Clear();
                NearbySetChanged?.Invoke();
            }

            return;
        }

        _currentFrameInteractables.Clear();

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            detectionRadius,
            detectionMask,
            queryTriggerInteraction);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            GenericInteractable interactable = hit.GetComponentInParent<GenericInteractable>();
            if (interactable == null)
            {
                continue;
            }

            if (!interactable.isActiveAndEnabled)
            {
                continue;
            }

            _currentFrameInteractables.Add(interactable);
        }

        bool changed = false;

        List<GenericInteractable> removedThisFrame = null;

        foreach (GenericInteractable interactable in _nearbyInteractables)
        {
            if (interactable != null && _currentFrameInteractables.Contains(interactable))
            {
                continue;
            }

            removedThisFrame ??= new List<GenericInteractable>();
            removedThisFrame.Add(interactable);
        }

        if (removedThisFrame != null)
        {
            for (int i = 0; i < removedThisFrame.Count; i++)
            {
                GenericInteractable interactable = removedThisFrame[i];
                string interactableName = GetSafeInteractableName(interactable);

                _nearbyInteractables.Remove(interactable);
                changed = true;

                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInteractionSensor] Removed nearby interactable '{interactableName}'.");
                }
            }
        }

        foreach (GenericInteractable interactable in _currentFrameInteractables)
        {
            if (interactable == null)
            {
                continue;
            }

            if (_nearbyInteractables.Add(interactable))
            {
                changed = true;

                if (verboseLogging)
                {
                    Debug.Log($"[PlayerInteractionSensor] Added nearby interactable '{GetSafeInteractableName(interactable)}'.");
                }
            }
        }

        if (changed)
        {
            NearbySetChanged?.Invoke();
        }
    }

    private string GetSafeInteractableName(GenericInteractable interactable)
    {
        return interactable == null ? "<destroyed>" : interactable.name;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
#endif
}