using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Creates or reuses a local camera for the owning client and attaches it to
/// the player prefab using a simple third-person MMO-style setup.
///
/// IMPORTANT:
/// - Cameras are local-only presentation objects.
/// - They should NOT be networked.
/// - Only the owning client should create/attach a camera to this player object.
///
/// DESIGN INTENT:
/// - The player prefab provides a CameraPivot transform in the hierarchy.
/// - The script positions the camera relative to that pivot.
/// - The pivot location is tuned in the prefab/editor.
/// - The camera behavior values (distance, pitch, FOV) are tuned in the Inspector.
/// </summary>
public class PlayerCameraBootstrap : NetworkBehaviour
{
    [Header("Camera Pivot")]
    [Tooltip("Transform on the player prefab that acts as the camera pivot point. Usually near the upper chest / base of neck.")]
    [SerializeField] private Transform cameraPivot;

    [Header("Camera Behavior")]
    [Tooltip("How far behind the pivot the camera should sit.")]
    [SerializeField] private float cameraDistance = 4.5f;

    [Tooltip("Downward pitch angle in degrees. Typical MMO values are around 12 to 18.")]
    [SerializeField] private float cameraPitch = 15f;

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
    /// 4. Apply a local offset and local pitch to create a natural third-person view.
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
        _attachedCamera.gameObject.tag = "MainCamera";

        // Apply the desired FOV.
        _attachedCamera.fieldOfView = cameraFieldOfView;

        Transform cameraTransform = _attachedCamera.transform;

        // Parent to the pivot.
        // Using 'false' means we want to work in local space relative to the pivot.
        cameraTransform.SetParent(cameraPivot, false);

        // Place the camera behind the pivot.
        // - Z is negative so the camera sits behind the player.
        // - X sideOffset lets you experiment later with slight shoulder offsets.
        // - Y stays 0 because the pivot itself defines the camera's vertical anchor point.
        cameraTransform.localPosition = new Vector3(sideOffset, 0f, -cameraDistance);

        // Aim the camera slightly downward.
        // This is the simplest first-pass third-person setup.
        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);

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