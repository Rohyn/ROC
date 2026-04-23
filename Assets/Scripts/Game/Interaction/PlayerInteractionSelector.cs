using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Selects the best nearby interactable from the set maintained by PlayerInteractionSensor.
///
/// This version supports interactables with any number of interaction focus points.
/// The nearest focus point to the player is used for facing/LOS,
/// while range/distance still uses the evaluation point (usually ClosestPoint on target collider).
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractionSelector : NetworkBehaviour
{
    [Header("Required References")]
    [SerializeField] private PlayerInteractionSensor interactionSensor;
    [SerializeField] private Transform cameraTransformOverride;

    [Header("Selection Timing")]
    [SerializeField] private float rescoreIntervalSeconds = 0.08f;

    [Header("Selection Origin")]
    [SerializeField] private float selectionOriginHeight = 1.0f;

    [Header("Facing Rules")]
    [Range(-1f, 1f)]
    [SerializeField] private float minimumFacingDot = 0.0f;

    [SerializeField] private float closeRangeOverrideDistance = 1.0f;

    [Header("Line Of Sight")]
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private LayerMask lineOfSightBlockers = ~0;

    [Header("Stickiness / Hysteresis")]
    [SerializeField] private float targetLostGraceSeconds = 0.18f;
    [SerializeField] private float switchScoreMargin = 8f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private Transform _cameraTransform;
    private float _nextRescoreTime;
    private float _currentTargetInvalidSince = -1f;

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
        Vector3 distancePoint = interactable.GetInteractionEvaluationPoint(origin);

        // Use the nearest focus point for facing / LOS / prompt relevance.
        Vector3 focusPoint = interactable.GetBestInteractionFocusPosition(origin);

        Vector3 toDistancePoint = distancePoint - origin;
        float distance = toDistancePoint.magnitude;
        if (distance <= 0.001f)
        {
            distance = 0.001f;
        }

        bool useCloseRangeOverride = distance <= closeRangeOverrideDistance;

        if (!useCloseRangeOverride)
        {
            Vector3 toFocusPoint = focusPoint - origin;
            float focusDistance = toFocusPoint.magnitude;
            if (focusDistance <= 0.001f)
            {
                focusDistance = 0.001f;
            }

            Vector3 directionToFocusPoint = toFocusPoint / focusDistance;

            float facingDot = Vector3.Dot(forward, directionToFocusPoint);
            if (facingDot < minimumFacingDot)
            {
                return float.NegativeInfinity;
            }

            if (requireLineOfSight && !HasLineOfSight(origin, focusPoint, interactable))
            {
                return float.NegativeInfinity;
            }

            return
                (facingDot * 100f) -
                (distance * 10f) +
                interactable.SelectionPriorityBonus;
        }

        return
            100f -
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