using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Selects the best nearby interactable from the set maintained by PlayerInteractionSensor.
///
/// This script does NOT perform broad physics searches.
/// It only scores already-known nearby candidates.
///
/// Selection is based on:
/// - candidate validity
/// - facing direction
/// - distance
/// - optional line of sight
///
/// This is intended to support:
/// - interaction prompts
/// - pressing E to use the current best target
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractionSelector : NetworkBehaviour
{
    [Header("Required References")]
    [Tooltip("Reference to the nearby-candidate sensor on this player.")]
    [SerializeField] private PlayerInteractionSensor interactionSensor;

    [Tooltip("Optional explicit camera transform. If empty, Camera.main is used.")]
    [SerializeField] private Transform cameraTransformOverride;

    [Header("Selection Timing")]
    [Tooltip("How often the selector should rescore nearby candidates, in seconds.")]
    [SerializeField] private float rescoreIntervalSeconds = 0.08f;

    [Header("Selection Origin")]
    [Tooltip("Vertical offset from the player root used as the origin for selection checks.")]
    [SerializeField] private float selectionOriginHeight = 1.0f;

    [Header("Facing Rules")]
    [Tooltip("Minimum dot product required for an interactable to count as being in front.")]
    [Range(-1f, 1f)]
    [SerializeField] private float minimumFacingDot = 0.0f;

    [Header("Line Of Sight")]
    [Tooltip("If true, require a clear line of sight to the candidate.")]
    [SerializeField] private bool requireLineOfSight = true;

    [Tooltip("Layers that can block line of sight to interactables.")]
    [SerializeField] private LayerMask lineOfSightBlockers = ~0;

    [Header("Debug")]
    [Tooltip("If true, selection changes will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private Transform _cameraTransform;
    private float _nextRescoreTime;

    public GenericInteractable CurrentTarget { get; private set; }

    public event Action<GenericInteractable> CurrentTargetChanged;

    private void Awake()
    {
        if (interactionSensor == null)
        {
            interactionSensor = GetComponent<PlayerInteractionSensor>();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        CacheCamera();

        if (interactionSensor != null)
        {
            interactionSensor.NearbySetChanged += HandleNearbySetChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (interactionSensor != null)
        {
            interactionSensor.NearbySetChanged -= HandleNearbySetChanged;
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (_cameraTransform == null)
        {
            CacheCamera();
        }

        if (interactionSensor == null)
        {
            return;
        }

        if (Time.time >= _nextRescoreTime)
        {
            RescoreCurrentTarget();
            _nextRescoreTime = Time.time + rescoreIntervalSeconds;
        }
    }

    public void ForceRefresh()
    {
        RescoreCurrentTarget();
        _nextRescoreTime = Time.time + rescoreIntervalSeconds;
    }

    private void HandleNearbySetChanged()
    {
        ForceRefresh();
    }

    private void RescoreCurrentTarget()
    {
        interactionSensor.CleanupNulls();

        Vector3 origin = GetSelectionOrigin();
        Vector3 forward = GetSelectionForward();

        GenericInteractable bestCandidate = null;
        float bestScore = float.NegativeInfinity;

        IReadOnlyCollection<GenericInteractable> candidates = interactionSensor.NearbyInteractables;

        foreach (GenericInteractable interactable in candidates)
        {
            if (interactable == null)
            {
                continue;
            }

            if (!interactable.CanInteract(gameObject))
            {
                continue;
            }

            float score = ScoreInteractable(origin, forward, interactable);

            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = interactable;
            }
        }

        if (bestCandidate != CurrentTarget)
        {
            CurrentTarget = bestCandidate;

            if (verboseLogging)
            {
                string targetName = CurrentTarget != null ? CurrentTarget.name : "null";
                Debug.Log($"[PlayerInteractionSelector] Current target changed to '{targetName}'.", this);
            }

            CurrentTargetChanged?.Invoke(CurrentTarget);
        }
    }

    private float ScoreInteractable(Vector3 origin, Vector3 forward, GenericInteractable interactable)
    {
        Vector3 targetPosition = interactable.InteractionFocusPosition;
        Vector3 toTarget = targetPosition - origin;

        float distance = toTarget.magnitude;
        if (distance <= 0.001f)
        {
            distance = 0.001f;
        }

        Vector3 directionToTarget = toTarget / distance;

        float facingDot = Vector3.Dot(forward, directionToTarget);
        if (facingDot < minimumFacingDot)
        {
            return float.NegativeInfinity;
        }

        if (requireLineOfSight && !HasLineOfSight(origin, targetPosition, interactable))
        {
            return float.NegativeInfinity;
        }

        float score = (facingDot * 100f) - (distance * 10f);
        return score;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 targetPosition, GenericInteractable candidate)
    {
        Vector3 toTarget = targetPosition - origin;
        float distance = toTarget.magnitude;

        if (distance <= 0.001f)
        {
            return true;
        }

        Vector3 direction = toTarget / distance;

        if (Physics.Raycast(
            origin,
            direction,
            out RaycastHit hit,
            distance,
            lineOfSightBlockers,
            QueryTriggerInteraction.Ignore))
        {
            GenericInteractable hitInteractable = hit.collider.GetComponentInParent<GenericInteractable>();
            return hitInteractable == candidate;
        }

        return true;
    }

    private Vector3 GetSelectionOrigin()
    {
        return transform.position + Vector3.up * selectionOriginHeight;
    }

    private Vector3 GetSelectionForward()
    {
        Transform referenceTransform = _cameraTransform != null ? _cameraTransform : transform;

        Vector3 forward = referenceTransform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        forward.Normalize();
        return forward;
    }

    private void CacheCamera()
    {
        if (cameraTransformOverride != null)
        {
            _cameraTransform = cameraTransformOverride;
            return;
        }

        if (Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }
    }
}