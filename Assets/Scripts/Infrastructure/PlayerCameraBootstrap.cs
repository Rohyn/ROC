using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Creates or reuses a local camera for the owning client and attaches it to
/// the player prefab.
///
/// IMPORTANT:
/// - Cameras are local-only presentation objects.
/// - They should NOT be networked.
/// - Only the owning client should create/attach a camera to this player object.
/// </summary>
public class PlayerCameraBootstrap : NetworkBehaviour
{
    [Header("Camera Attachment")]
    [Tooltip("Transform on the player prefab that acts as the camera mount point.")]
    [SerializeField] private Transform cameraSocket;

    [Tooltip("If no Main Camera exists, a new camera will be created automatically.")]
    [SerializeField] private bool createCameraIfMissing = true;

    [Tooltip("Used only when a new camera is created at runtime.")]
    [SerializeField] private float fallbackFieldOfView = 60f;

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
    /// </summary>
    private void AttachLocalCamera()
    {
        if (cameraSocket == null)
        {
            Debug.LogError("[PlayerCameraBootstrap] No cameraSocket assigned on the player prefab.");
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
            cameraToUse.fieldOfView = fallbackFieldOfView;

            // Every scene should only have one active AudioListener.
            cameraObject.AddComponent<AudioListener>();

            _createdCameraAtRuntime = true;
        }

        _attachedCamera = cameraToUse;

        // Parent the camera to the socket so it follows the player.
        Transform cameraTransform = _attachedCamera.transform;
        cameraTransform.SetParent(cameraSocket, false);
        cameraTransform.localPosition = Vector3.zero;
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