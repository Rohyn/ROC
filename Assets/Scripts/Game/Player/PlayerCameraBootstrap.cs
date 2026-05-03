using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Creates or reuses a local camera for the owning client and attaches a
/// collision-safe third-person camera rig to it.
/// 
/// IMPORTANT:
/// - Cameras are local-only presentation objects.
/// - Cameras should not be networked.
/// - Only the owning client should create/attach/configure a camera.
/// - PlayerLookController still owns yaw/pitch and cursor state.
/// - PlayerCameraSafetyRig owns camera placement/collision.
/// </summary>
public class PlayerCameraBootstrap : NetworkBehaviour
{
    [Header("Camera Pivot")]
    [Tooltip("Transform on the player prefab that acts as the camera pivot point. Usually near upper chest / base of neck.")]
    [SerializeField] private Transform cameraPivot;

    [Header("Camera Placement")]
    [Tooltip("How far behind the pivot the camera should sit when unobstructed.")]
    [SerializeField] private float cameraDistance = 4.5f;

    [Tooltip("How far above the pivot the camera should sit when unobstructed.")]
    [SerializeField] private float verticalOffset = 0.5f;

    [Tooltip("Optional left/right offset. Leave at 0 for centered MMO-style camera.")]
    [SerializeField] private float sideOffset = 0f;

    [Tooltip("The camera's vertical field of view in degrees.")]
    [SerializeField] private float cameraFieldOfView = 65f;

    [Header("Camera Collision")]
    [Tooltip("Layers that should block the camera. Use world/architecture/props. Exclude Player, UI, and trigger-only interaction layers.")]
    [SerializeField] private LayerMask obstructionMask = ~0;

    [Tooltip("Radius used for camera sphere-cast collision.")]
    [SerializeField] private float collisionRadius = 0.25f;

    [Tooltip("Small distance to keep the camera away from walls after collision.")]
    [SerializeField] private float surfacePadding = 0.08f;

    [Tooltip("Closest allowed distance from pivot/cast origin to camera.")]
    [SerializeField] private float minimumCameraDistance = 0.45f;

    [Tooltip("How quickly the camera moves inward when blocked.")]
    [SerializeField] private float blockedSmoothTime = 0.035f;

    [Tooltip("How quickly the camera returns outward when clear.")]
    [SerializeField] private float restoreSmoothTime = 0.12f;

    [Tooltip("Near clip plane for the local camera. Lower helps tight interiors.")]
    [SerializeField] private float nearClipPlane = 0.03f;

    [Header("Camera Creation")]
    [Tooltip("If no Main Camera exists, a new camera will be created automatically.")]
    [SerializeField] private bool createCameraIfMissing = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private Camera _attachedCamera;
    private PlayerCameraSafetyRig _safetyRig;
    private bool _createdCameraAtRuntime;
    private bool _addedSafetyRigAtRuntime;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            return;
        }

        AttachLocalCamera();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            return;
        }

        DetachOrDestroyLocalCamera();
    }

    private void AttachLocalCamera()
    {
        if (cameraPivot == null)
        {
            Debug.LogError("[PlayerCameraBootstrap] No cameraPivot assigned on the player prefab.", this);
            return;
        }

        Camera cameraToUse = Camera.main;

        if (cameraToUse == null)
        {
            if (!createCameraIfMissing)
            {
                Debug.LogWarning("[PlayerCameraBootstrap] No Main Camera found and camera creation is disabled.", this);
                return;
            }

            GameObject cameraObject = new GameObject("Local Player Camera");
            cameraToUse = cameraObject.AddComponent<Camera>();

            if (FindFirstObjectByType<AudioListener>() == null)
            {
                cameraObject.AddComponent<AudioListener>();
            }

            _createdCameraAtRuntime = true;
        }

        _attachedCamera = cameraToUse;
        _attachedCamera.gameObject.tag = "MainCamera";
        _attachedCamera.fieldOfView = Mathf.Clamp(cameraFieldOfView, 30f, 100f);
        _attachedCamera.nearClipPlane = Mathf.Max(0.01f, nearClipPlane);

        EnsureAudioListener(_attachedCamera);

        Transform cameraTransform = _attachedCamera.transform;
        cameraTransform.SetParent(cameraPivot, false);
        cameraTransform.localPosition = new Vector3(sideOffset, verticalOffset, -cameraDistance);
        cameraTransform.localRotation = Quaternion.identity;

        _safetyRig = _attachedCamera.GetComponent<PlayerCameraSafetyRig>();

        if (_safetyRig == null)
        {
            _safetyRig = _attachedCamera.gameObject.AddComponent<PlayerCameraSafetyRig>();
            _addedSafetyRigAtRuntime = true;
        }

        _safetyRig.Configure(
            cameraPivot,
            transform,
            _attachedCamera,
            new Vector3(sideOffset, verticalOffset, -cameraDistance),
            obstructionMask,
            collisionRadius,
            surfacePadding,
            minimumCameraDistance,
            blockedSmoothTime,
            restoreSmoothTime,
            nearClipPlane);

        if (verboseLogging)
        {
            Debug.Log("[PlayerCameraBootstrap] Attached collision-safe local camera to owning player.", this);
        }
    }

    private void DetachOrDestroyLocalCamera()
    {
        if (_safetyRig != null)
        {
            _safetyRig.Clear();

            if (_addedSafetyRigAtRuntime)
            {
                Destroy(_safetyRig);
            }
        }

        _safetyRig = null;
        _addedSafetyRigAtRuntime = false;

        if (_attachedCamera == null)
        {
            return;
        }

        if (_createdCameraAtRuntime)
        {
            Destroy(_attachedCamera.gameObject);
        }
        else
        {
            _attachedCamera.transform.SetParent(null, true);
        }

        _attachedCamera = null;
        _createdCameraAtRuntime = false;
    }

    private static void EnsureAudioListener(Camera cameraToUse)
    {
        if (cameraToUse == null)
        {
            return;
        }

        if (cameraToUse.GetComponent<AudioListener>() != null)
        {
            return;
        }

        if (FindFirstObjectByType<AudioListener>() != null)
        {
            return;
        }

        cameraToUse.gameObject.AddComponent<AudioListener>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        cameraDistance = Mathf.Max(0.25f, cameraDistance);
        cameraFieldOfView = Mathf.Clamp(cameraFieldOfView, 30f, 100f);
        collisionRadius = Mathf.Max(0.01f, collisionRadius);
        surfacePadding = Mathf.Max(0f, surfacePadding);
        minimumCameraDistance = Mathf.Max(0.05f, minimumCameraDistance);
        blockedSmoothTime = Mathf.Max(0.001f, blockedSmoothTime);
        restoreSmoothTime = Mathf.Max(0.001f, restoreSmoothTime);
        nearClipPlane = Mathf.Max(0.01f, nearClipPlane);
    }
#endif
}