using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Creates or reuses a local camera for the owning client and attaches it to
/// the player prefab using a simple third-person camera rig.
///
/// IMPORTANT:
/// - Cameras are local-only presentation objects.
/// - They should NOT be networked.
/// - Only the owning client should create/attach a camera to this player object.
///
/// DESIGN INTENT:
/// - The player prefab provides a CameraPivot transform in the hierarchy.
/// - This script positions the camera relative to that pivot.
/// - The pivot itself is responsible for pitch rotation via a separate look controller.
/// - This script only handles camera placement, FOV, and ownership/cleanup.
/// </summary>
public class PlayerCameraBootstrap : NetworkBehaviour
{
    [Header("Camera Pivot")]
    [Tooltip("Transform on the player prefab that acts as the camera pivot point. Usually near the upper chest / base of neck.")]
    [SerializeField] private Transform cameraPivot;

    [Header("Camera Placement")]
    [Tooltip("How far behind the pivot the camera should sit.")]
    [SerializeField] private float cameraDistance = 4.5f;

    [Tooltip("How far above the pivot the camera should sit.")]
    [SerializeField] private float verticalOffset = 0.5f;

    [Tooltip("Optional left/right offset. Leave at 0 for a centered MMO-style camera.")]
    [SerializeField] private float sideOffset = 0f;

    [Tooltip("The camera's vertical field of view in degrees.")]
    [SerializeField] private float cameraFieldOfView = 65f;

    [Header("Camera Creation")]
    [Tooltip("If no Main Camera exists, a new camera will be created automatically.")]
    [SerializeField] private bool createCameraIfMissing = true;

    private Camera _attachedCamera;
    private bool _createdCameraAtRuntime;

    public override void OnNetworkSpawn()
    {
        // Only the owning client should attach a local camera.
        if (!IsOwner)
        {
            return;
        }

        AttachLocalCamera();
    }

    public override void OnNetworkDespawn()
    {
        // Only clean up a camera on the owning client.
        if (!IsOwner)
        {
            return;
        }

        DetachOrDestroyLocalCamera();
    }

    /// <summary>
    /// Attaches a camera to the player.
    ///
    /// Preferred behavior:
    /// 1. Reuse an existing Main Camera if one already exists.
    /// 2. Otherwise create a new local camera.
    /// 3. Parent the camera to the pivot.
    /// 4. Apply a local offset behind and above the pivot.
    ///
    /// IMPORTANT:
    /// This script does NOT apply pitch.
    /// Pitch should be controlled by a separate look controller that rotates the pivot.
    /// </summary>
    private void AttachLocalCamera()
    {
        if (cameraPivot == null)
        {
            Debug.LogError("[PlayerCameraBootstrap] No cameraPivot assigned on the player prefab.");
            return;
        }

        Camera cameraToUse = Camera.main;

        if (cameraToUse == null)
        {
            if (!createCameraIfMissing)
            {
                Debug.LogWarning("[PlayerCameraBootstrap] No Main Camera found and camera creation is disabled.");
                return;
            }

            GameObject cameraObject = new GameObject("Local Player Camera");
            cameraToUse = cameraObject.AddComponent<Camera>();

            // Every scene should only have one active AudioListener.
            cameraObject.AddComponent<AudioListener>();

            _createdCameraAtRuntime = true;
        }

        _attachedCamera = cameraToUse;

        // Ensure other systems can find this camera via Camera.main.
        _attachedCamera.gameObject.tag = "MainCamera";

        // Apply the desired field of view.
        _attachedCamera.fieldOfView = cameraFieldOfView;

        Transform cameraTransform = _attachedCamera.transform;

        // Parent to the pivot.
        // Using false means we want to work in local space relative to the pivot.
        cameraTransform.SetParent(cameraPivot, false);

        // Place the camera behind and slightly above the pivot.
        // The pivot itself will handle pitch/yaw behavior elsewhere.
        cameraTransform.localPosition = new Vector3(sideOffset, verticalOffset, -cameraDistance);

        // Keep the camera's local rotation neutral.
        // The pivot's rotation should determine the final viewing angle.
        cameraTransform.localRotation = Quaternion.identity;

        Debug.Log("[PlayerCameraBootstrap] Attached local camera to owning player.");
    }

    /// <summary>
    /// Cleans up camera ownership when the player despawns.
    ///
    /// If we created the camera ourselves, we destroy it.
    /// If we reused an existing scene camera, we simply detach it.
    /// </summary>
    private void DetachOrDestroyLocalCamera()
    {
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
}