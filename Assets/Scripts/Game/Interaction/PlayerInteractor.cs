using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Handles the owning player's interaction input.
///
/// First-pass behavior:
/// - casts forward from the current camera
/// - finds a GenericInteractable
/// - calls TryInteract when E is pressed
///
/// This is intentionally simple and local for now because your current movement
/// and status systems are still local-owner logic.
///
/// Later, this is the place where you can switch to:
/// - client request -> server RPC
/// - server validation
/// - UI prompts
/// - focus highlighting
/// </summary>
public class PlayerInteractor : NetworkBehaviour
{
    [Header("Interaction Query")]
    [Tooltip("Maximum raycast distance for finding interactables.")]
    [SerializeField] private float interactDistance = 3f;

    [Tooltip("Layer mask used to limit what the interaction raycast can hit.")]
    [SerializeField] private LayerMask interactableMask = ~0;

    [Tooltip("Optional explicit camera transform. If empty, Camera.main is used.")]
    [SerializeField] private Transform cameraTransformOverride;

    private Transform _cameraTransform;

    private void Start()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        CacheCamera();
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

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            TryInteractForward();
        }
    }

    /// <summary>
    /// Finds the first interactable in front of the player camera and tries to use it.
    /// </summary>
	private void TryInteractForward()
	{
	    if (_cameraTransform == null)
	    {
	        Debug.LogWarning("[PlayerInteractor] No camera transform available for interaction query.");
	        return;
	    }

	    // Start the interaction sweep from the player, not from the camera.
	    // A point around lower chest / upper waist height is usually a good interaction origin.
	    Vector3 origin = transform.position + Vector3.up * 1.0f;

	    // Use the camera's facing direction, but flatten it onto the ground plane.
	    // This makes "forward" match what the player sees without aiming sharply up/down.
	    Vector3 direction = _cameraTransform.forward;
	    direction.y = 0f;
	    direction.Normalize();

	    // Fallback safety in case the camera is looking almost straight up/down.
	    if (direction.sqrMagnitude < 0.0001f)
	    {
	        direction = transform.forward;
	    }

	    // SphereCast is more forgiving than a thin ray for third-person interaction.
	    if (!Physics.SphereCast(
	            origin,
	            0.35f,
	            direction,
	            out RaycastHit hit,
	            interactDistance,
	            interactableMask,
	            QueryTriggerInteraction.Ignore))
	    {
	        Debug.Log("[PlayerInteractor] SphereCast hit nothing.");
	        Debug.DrawRay(origin, direction * interactDistance, Color.red, 1.0f);
	        return;
	    }

	    Debug.Log($"[PlayerInteractor] SphereCast hit '{hit.collider.name}'.");
	    Debug.DrawRay(origin, direction * hit.distance, Color.green, 1.0f);

	    GenericInteractable interactable = hit.collider.GetComponentInParent<GenericInteractable>();
	    if (interactable == null)
	    {
	        Debug.Log("[PlayerInteractor] Hit collider has no GenericInteractable in parents.");
	        return;
	    }

	    Debug.Log($"[PlayerInteractor] Found interactable '{interactable.name}'. Trying interaction.");

	    bool success = interactable.TryInteract(gameObject);
	    Debug.Log($"[PlayerInteractor] Interaction success = {success}");
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