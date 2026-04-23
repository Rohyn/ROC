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
/// - optional selection priority
///
/// This version includes:
/// - support for wide interaction target colliders
/// - sticky current-target behavior
/// - close-range override for objects that are right beside the player
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

    [Tooltip("If the interactable is this close or closer, facing rules are relaxed.")]
    [SerializeField] private float closeRangeOverrideDistance = 1.0f;

    [Header("Line Of Sight")]
    [Tooltip("If true, require a clear line of sight to the candidate.")]
    [SerializeField] private bool requireLineOfSight = true;

    [Tooltip("Layers that can block line of sight to interactables.")]
    [SerializeField] private LayerMask lineOfSightBlockers = ~0;

    [Header("Stickiness / Hysteresis")]
    [Tooltip("How long the current target is allowed to remain selected after momentarily becoming invalid.")]
    [SerializeField] private float targetLostGraceSeconds = 0.18f;

    [Tooltip("How much better a new candidate must score before replacing the current target.")]
    [SerializeField] private float switchScoreMargin = 8f;

    [Header("Debug")]
    [Tooltip("If true, selection changes will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private Transform _cameraTransform;
    private float _nextRescoreTime;

    /// <summary>
    /// Time when the current target first became invalid.
    /// Used for grace-period stickiness.
    /// </summary>
    private float _currentTargetInvalidSince = -1f;

    /// <summary>
    /// The interactable currently considered the best candidate.
    /// </summary>
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
        float currentTargetScore = float.NegativeInfinity;

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

            if (interactable == CurrentTarget)
            {
                currentTargetScore = score;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = interactable;
            }
        }

        bool currentTargetIsValid = CurrentTarget != null && currentTargetScore > float.NegativeInfinity;

        if (currentTargetIsValid)
        {
            _currentTargetInvalidSince = -1f;

            if (bestCandidate == null || bestCandidate == CurrentTarget)
            {
                return;
            }

            if (bestScore < currentTargetScore + switchScoreMargin)
            {
                return;
            }

            SetCurrentTarget(bestCandidate);
            return;
        }

        if (CurrentTarget != null && !currentTargetIsValid)
        {
            if (_currentTargetInvalidSince < 0f)
            {
                _currentTargetInvalidSince = Time.time;
            }

            float invalidDuration = Time.time - _currentTargetInvalidSince;
            if (invalidDuration < targetLostGraceSeconds)
            {
                return;
            }

            _currentTargetInvalidSince = -1f;
            SetCurrentTarget(bestCandidate);
            return;
        }

        _currentTargetInvalidSince = -1f;
        SetCurrentTarget(bestCandidate);
    }

    private float ScoreInteractable(Vector3 origin, Vector3 forward, GenericInteractable interactable)
    {
        // Use the best available point for distance evaluation.
        Vector3 distancePoint = interactable.GetInteractionEvaluationPoint(origin);

        // Use a more stable point for facing / LOS.
        // This avoids the "closest point is beside me, so I can't interact" problem
        // on wide objects like beds.
        Vector3 facingPoint = interactable.InteractionFocusPosition;

        Vector3 toDistancePoint = distancePoint - origin;
        float distance = toDistancePoint.magnitude;
        if (distance <= 0.001f)
        {
            distance = 0.001f;
        }

        // Close-range override:
        // if the object is very near, do not require strict facing.
        bool useCloseRangeOverride = distance <= closeRangeOverrideDistance;

        if (!useCloseRangeOverride)
        {
            Vector3 toFacingPoint = facingPoint - origin;
            float facingDistance = toFacingPoint.magnitude;
            if (facingDistance <= 0.001f)
            {
                facingDistance = 0.001f;
            }

            Vector3 directionToFacingPoint = toFacingPoint / facingDistance;

            float facingDot = Vector3.Dot(forward, directionToFacingPoint);
            if (facingDot < minimumFacingDot)
            {
                return float.NegativeInfinity;
            }

            if (requireLineOfSight && !HasLineOfSight(origin, facingPoint, interactable))
            {
                return float.NegativeInfinity;
            }

            return
                (facingDot * 100f) -
                (distance * 10f) +
                interactable.SelectionPriorityBonus;
        }

        // If very close, treat it as eligible even if the facing point is not ideal.
        // Distance still matters, and we keep the interactable's priority bonus.
        return
            (100f) -
            (distance * 10f) +
            interactable.SelectionPriorityBonus;
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

    private void SetCurrentTarget(GenericInteractable newTarget)
    {
        if (newTarget == CurrentTarget)
        {
            return;
        }

        CurrentTarget = newTarget;

        if (verboseLogging)
        {
            string targetName = CurrentTarget != null ? CurrentTarget.name : "null";
            Debug.Log($"[PlayerInteractionSelector] Current target changed to '{targetName}'.", this);
        }

        CurrentTargetChanged?.Invoke(CurrentTarget);
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