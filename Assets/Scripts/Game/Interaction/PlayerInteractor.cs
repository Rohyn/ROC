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
	        Debug.LogWarning("[PlayerInteractor] No camera transform available for interaction raycast.");
	        return;
	    }

	    Ray ray = new Ray(_cameraTransform.position, _cameraTransform.forward);

	    if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactableMask, QueryTriggerInteraction.Ignore))
	    {
	        Debug.Log("[PlayerInteractor] Raycast hit nothing.");
	        return;
	    }

	    Debug.Log($"[PlayerInteractor] Raycast hit '{hit.collider.name}'.");

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