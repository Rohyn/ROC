using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Handles the owning player's interaction input.
///
/// This version uses the local selector for UX/targeting,
/// but sends the actual interaction request to the server.
///
/// That means:
/// - client decides what it is trying to interact with
/// - server validates range via GenericInteractable.CanInteract(...)
/// - server executes the action chain authoritatively
///
/// This keeps prompt/selection responsive while making interaction authoritative.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractor : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the interaction selector on this player. If left empty, it will be found automatically.")]
    [SerializeField] private PlayerInteractionSelector interactionSelector;

    [Header("Debug")]
    [Tooltip("If true, interaction attempts and failures will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private void Awake()
    {
        if (interactionSelector == null)
        {
            interactionSelector = GetComponent<PlayerInteractionSelector>();
        }
    }

    private void Update()
    {
        // Only the owning client should read local input.
        if (!IsOwner)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            RequestInteractCurrentTarget();
        }
    }

    private void RequestInteractCurrentTarget()
    {
        if (interactionSelector == null)
        {
            Debug.LogWarning("[PlayerInteractor] No PlayerInteractionSelector assigned.");
            return;
        }

        GenericInteractable target = interactionSelector.CurrentTarget;
        if (target == null)
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerInteractor] No current interactable target.");
            }

            return;
        }

        NetworkObject targetNetworkObject = target.GetComponent<NetworkObject>();
        if (targetNetworkObject == null)
        {
            Debug.LogWarning($"[PlayerInteractor] Target '{target.name}' has no NetworkObject. Server interaction requires one.");
            return;
        }

        if (IsServer)
        {
            PerformInteractionOnServer(targetNetworkObject);
            return;
        }

        RequestInteractRpc(new NetworkObjectReference(targetNetworkObject));
    }

    /// <summary>
    /// Client -> Server interaction request.
    /// </summary>
    [Rpc(SendTo.Server)]
    private void RequestInteractRpc(NetworkObjectReference targetReference)
    {
        if (!targetReference.TryGet(out NetworkObject targetNetworkObject, NetworkManager))
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerInteractor] Server could not resolve NetworkObjectReference for interaction.");
            }

            return;
        }

        PerformInteractionOnServer(targetNetworkObject);
    }

    /// <summary>
    /// Runs on the server and executes the interaction if it is valid.
    /// </summary>
    private void PerformInteractionOnServer(NetworkObject targetNetworkObject)
    {
        if (targetNetworkObject == null)
        {
            return;
        }

        GenericInteractable interactable = targetNetworkObject.GetComponent<GenericInteractable>();
        if (interactable == null)
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerInteractor] Target NetworkObject has no GenericInteractable.");
            }

            return;
        }

        // First-pass server validation:
        // only validate authoritative proximity/range through CanInteract.
        // This is enough for now and keeps the architecture simple.
        if (!interactable.CanInteract(gameObject))
        {
            if (verboseLogging)
            {
                Debug.Log($"[PlayerInteractor] Server rejected interaction with '{interactable.name}' because CanInteract returned false.");
            }

            return;
        }

        bool success = interactable.TryInteract(gameObject);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerInteractor] Server processed interaction with '{interactable.name}'. Success={success}");
        }
    }
}