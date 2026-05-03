using UnityEngine;

/// <summary>
/// Local-only third-person camera collision/safety rig.
/// 
/// This component should live on the local player's Camera object.
/// It does not read input and does not network anything.
/// 
/// Responsibilities:
/// - keep camera aimed with the player's camera pivot
/// - place the camera at the desired third-person offset
/// - sphere-cast from pivot to desired camera position
/// - smoothly pull the camera inward when walls/ceilings/props block the view
/// - smoothly restore camera distance when obstruction clears
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCameraSafetyRig : MonoBehaviour
{
    [Header("Required Runtime References")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform ownerRoot;
    [SerializeField] private Camera targetCamera;

    [Header("Desired Third-Person Offset")]
    [Tooltip("Camera's desired local offset relative to CameraPivot.")]
    [SerializeField] private Vector3 desiredLocalOffset = new Vector3(0f, 0.5f, -4.5f);

    [Tooltip("Sphere cast starts from this local offset relative to CameraPivot. Usually near upper chest/head.")]
    [SerializeField] private Vector3 castOriginLocalOffset = new Vector3(0f, 0.25f, 0f);

    [Header("Collision")]
    [Tooltip("Layers that should block the camera. Use world/architecture/props. Exclude Player, UI, and trigger-only interaction layers.")]
    [SerializeField] private LayerMask obstructionMask = ~0;

    [Tooltip("Radius of the camera collision sphere. Larger prevents wall-peeking more aggressively.")]
    [SerializeField] private float collisionRadius = 0.25f;

    [Tooltip("Small distance to keep camera away from the blocking surface.")]
    [SerializeField] private float surfacePadding = 0.08f;

    [Tooltip("Closest allowed distance from cast origin to camera.")]
    [SerializeField] private float minimumDistance = 0.45f;

    [Tooltip("Ignore trigger colliders such as interaction volumes.")]
    [SerializeField] private bool ignoreTriggers = true;

    [Tooltip("Ignore colliders under ownerRoot so the player capsule/body does not block the camera.")]
    [SerializeField] private bool ignoreOwnerColliders = true;

    [Header("Smoothing")]
    [Tooltip("How quickly the camera moves inward when blocked.")]
    [SerializeField] private float blockedSmoothTime = 0.035f;

    [Tooltip("How quickly the camera returns outward when no longer blocked.")]
    [SerializeField] private float restoreSmoothTime = 0.12f;

    [Header("Camera")]
    [Tooltip("Useful for tight interiors. Lower near clip reduces the chance of nearby walls slicing into view.")]
    [SerializeField] private float nearClipPlane = 0.03f;

    [Tooltip("If true, this script forces camera rotation to match CameraPivot every LateUpdate.")]
    [SerializeField] private bool forcePivotRotation = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;
    [SerializeField] private bool drawDebugGizmos = false;

    private const int MaxHits = 16;
    private readonly RaycastHit[] _hits = new RaycastHit[MaxHits];

    private Transform _cameraTransform;
    private float _currentDistance;
    private float _distanceVelocity;
    private bool _isConfigured;

    private Vector3 _lastCastOrigin;
    private Vector3 _lastDesiredPosition;
    private Vector3 _lastResolvedPosition;
    private bool _lastWasBlocked;

    public void Configure(
        Transform pivot,
        Transform owner,
        Camera cameraToControl,
        Vector3 desiredOffset,
        LayerMask cameraObstructionMask,
        float radius,
        float padding,
        float minDistance,
        float blockedSmooth,
        float restoreSmooth,
        float clipPlane)
    {
        cameraPivot = pivot;
        ownerRoot = owner;
        targetCamera = cameraToControl;
        desiredLocalOffset = desiredOffset;
        obstructionMask = cameraObstructionMask;
        collisionRadius = Mathf.Max(0.01f, radius);
        surfacePadding = Mathf.Max(0f, padding);
        minimumDistance = Mathf.Max(0.05f, minDistance);
        blockedSmoothTime = Mathf.Max(0.001f, blockedSmooth);
        restoreSmoothTime = Mathf.Max(0.001f, restoreSmooth);
        nearClipPlane = Mathf.Max(0.01f, clipPlane);

        _cameraTransform = targetCamera != null ? targetCamera.transform : transform;
        ApplyCameraSettings();
        ResetRuntimeDistance();

        _isConfigured = cameraPivot != null && _cameraTransform != null;

        if (verboseLogging)
        {
            Debug.Log(
                _isConfigured
                    ? "[PlayerCameraSafetyRig] Configured camera safety rig."
                    : "[PlayerCameraSafetyRig] Configuration incomplete.",
                this);
        }
    }

    public void Clear()
    {
        _isConfigured = false;
        cameraPivot = null;
        ownerRoot = null;
        targetCamera = null;
        _cameraTransform = null;
        _distanceVelocity = 0f;
    }

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        _cameraTransform = targetCamera != null ? targetCamera.transform : transform;
        ApplyCameraSettings();
        ResetRuntimeDistance();

        _isConfigured = cameraPivot != null && _cameraTransform != null;
    }

    private void OnEnable()
    {
        ApplyCameraSettings();
        ResetRuntimeDistance();
    }

    private void LateUpdate()
    {
        if (!_isConfigured)
        {
            TrySelfConfigure();
        }

        if (!_isConfigured || cameraPivot == null || _cameraTransform == null)
        {
            return;
        }

        ResolveCameraPosition();

        if (forcePivotRotation)
        {
            _cameraTransform.rotation = cameraPivot.rotation;
        }
    }

    private void TrySelfConfigure()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        _cameraTransform = targetCamera != null ? targetCamera.transform : transform;
        _isConfigured = cameraPivot != null && _cameraTransform != null;

        if (_isConfigured)
        {
            ApplyCameraSettings();
            ResetRuntimeDistance();
        }
    }

    private void ResolveCameraPosition()
    {
        Vector3 castOrigin = cameraPivot.TransformPoint(castOriginLocalOffset);
        Vector3 desiredPosition = cameraPivot.TransformPoint(desiredLocalOffset);
        Vector3 desiredVector = desiredPosition - castOrigin;

        float desiredDistance = desiredVector.magnitude;

        if (desiredDistance <= 0.001f)
        {
            _cameraTransform.position = desiredPosition;
            return;
        }

        Vector3 castDirection = desiredVector / desiredDistance;

        bool blocked = TryFindNearestValidObstruction(
            castOrigin,
            castDirection,
            desiredDistance,
            out RaycastHit nearestHit);

        float targetDistance = desiredDistance;

        if (blocked)
        {
            targetDistance = Mathf.Clamp(
                nearestHit.distance - surfacePadding,
                minimumDistance,
                desiredDistance);
        }

        float smoothTime = targetDistance < _currentDistance
            ? blockedSmoothTime
            : restoreSmoothTime;

        _currentDistance = Mathf.SmoothDamp(
            _currentDistance,
            targetDistance,
            ref _distanceVelocity,
            smoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);

        _currentDistance = Mathf.Clamp(_currentDistance, minimumDistance, desiredDistance);

        Vector3 resolvedPosition = castOrigin + castDirection * _currentDistance;
        _cameraTransform.position = resolvedPosition;

        _lastCastOrigin = castOrigin;
        _lastDesiredPosition = desiredPosition;
        _lastResolvedPosition = resolvedPosition;
        _lastWasBlocked = blocked;
    }

    private bool TryFindNearestValidObstruction(
        Vector3 origin,
        Vector3 direction,
        float distance,
        out RaycastHit nearestHit)
    {
        nearestHit = default;

        QueryTriggerInteraction triggerInteraction = ignoreTriggers
            ? QueryTriggerInteraction.Ignore
            : QueryTriggerInteraction.Collide;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            collisionRadius,
            direction,
            _hits,
            distance,
            obstructionMask,
            triggerInteraction);

        bool found = false;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hits[i];

            if (hit.collider == null)
            {
                continue;
            }

            if (!IsValidObstruction(hit.collider))
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private bool IsValidObstruction(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        Transform colliderTransform = collider.transform;

        if (ignoreOwnerColliders && ownerRoot != null && colliderTransform.IsChildOf(ownerRoot))
        {
            return false;
        }

        if (_cameraTransform != null && colliderTransform.IsChildOf(_cameraTransform))
        {
            return false;
        }

        return true;
    }

    private void ApplyCameraSettings()
    {
        if (targetCamera == null)
        {
            return;
        }

        targetCamera.nearClipPlane = Mathf.Max(0.01f, nearClipPlane);
    }

    private void ResetRuntimeDistance()
    {
        Vector3 castOrigin = cameraPivot != null
            ? cameraPivot.TransformPoint(castOriginLocalOffset)
            : Vector3.zero;

        Vector3 desiredPosition = cameraPivot != null
            ? cameraPivot.TransformPoint(desiredLocalOffset)
            : desiredLocalOffset;

        _currentDistance = Mathf.Max(minimumDistance, Vector3.Distance(castOrigin, desiredPosition));
        _distanceVelocity = 0f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        collisionRadius = Mathf.Max(0.01f, collisionRadius);
        surfacePadding = Mathf.Max(0f, surfacePadding);
        minimumDistance = Mathf.Max(0.05f, minimumDistance);
        blockedSmoothTime = Mathf.Max(0.001f, blockedSmoothTime);
        restoreSmoothTime = Mathf.Max(0.001f, restoreSmoothTime);
        nearClipPlane = Mathf.Max(0.01f, nearClipPlane);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(_lastCastOrigin, collisionRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_lastDesiredPosition, collisionRadius);
        Gizmos.DrawLine(_lastCastOrigin, _lastDesiredPosition);

        Gizmos.color = _lastWasBlocked ? Color.red : Color.green;
        Gizmos.DrawWireSphere(_lastResolvedPosition, collisionRadius);
        Gizmos.DrawLine(_lastCastOrigin, _lastResolvedPosition);
    }
}