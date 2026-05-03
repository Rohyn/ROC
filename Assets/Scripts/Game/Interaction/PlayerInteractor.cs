using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles the owning player's interaction input.
/// 
/// This version uses configurable keybinds through RocInput.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractor : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the interaction selector on this player. If left empty, it will be found automatically.")]
    [SerializeField] private PlayerInteractionSelector interactionSelector;

    [Header("Fallback Input")]
    [Tooltip("Used only if settings cannot resolve the Interact binding.")]
    [SerializeField] private Key fallbackInteractKey = Key.E;

    [Header("Debug")]
    [Tooltip("If true, interaction attempts and failures will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerAreaStreamingController _areaStreamingController;

    private void Awake()
    {
        if (interactionSelector == null)
        {
            interactionSelector = GetComponent<PlayerInteractionSelector>();
        }

        _areaStreamingController = GetComponent<PlayerAreaStreamingController>();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (RocInput.WasPressedThisFrame(KeybindActionId.Interact, fallbackInteractKey))
        {
            RequestInteractCurrentTarget();
        }
    }

    private void RequestInteractCurrentTarget()
    {
        if (interactionSelector == null)
        {
            Debug.LogWarning("[PlayerInteractor] No PlayerInteractionSelector assigned.", this);
            return;
        }

        GenericInteractable target = interactionSelector.CurrentTarget;

        if (target == null)
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerInteractor] No current interactable target.", this);
            }

            return;
        }

        string interactableId = target.InteractableId;

        if (string.IsNullOrWhiteSpace(interactableId))
        {
            Debug.LogWarning($"[PlayerInteractor] Target '{target.name}' has no usable InteractableId.", target);
            return;
        }

        if (IsServer)
        {
            PerformInteractionByIdOnServer(interactableId);
            return;
        }

        RequestInteractByIdRpc(interactableId);
    }

    [Rpc(SendTo.Server)]
    private void RequestInteractByIdRpc(string interactableId)
    {
        PerformInteractionByIdOnServer(interactableId);
    }

    private void PerformInteractionByIdOnServer(string interactableId)
    {
        if (!IsServer)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(interactableId))
        {
            return;
        }

        string areaSceneName = GetAuthoritativeAreaSceneName();

        if (string.IsNullOrWhiteSpace(areaSceneName))
        {
            if (verboseLogging)
            {
                Debug.LogWarning("[PlayerInteractor] Could not resolve authoritative area scene for interaction.", this);
            }

            return;
        }

        if (!InteractableRegistry.TryGet(areaSceneName, interactableId, out GenericInteractable interactable))
        {
            if (verboseLogging)
            {
                Debug.LogWarning(
                    $"[PlayerInteractor] Server could not resolve interactable id '{interactableId}' in area '{areaSceneName}'.",
                    this);
            }

            return;
        }

        PerformInteractionOnServer(interactable);
    }

    private void PerformInteractionOnServer(GenericInteractable interactable)
    {
        if (!IsServer)
        {
            return;
        }

        if (interactable == null)
        {
            return;
        }

        if (!interactable.CanInteract(gameObject))
        {
            if (verboseLogging)
            {
                Debug.Log(
                    $"[PlayerInteractor] Server rejected interaction with '{interactable.name}' " +
                    $"id='{interactable.InteractableId}' because CanInteract returned false.",
                    interactable);
            }

            return;
        }

        bool success = interactable.TryInteract(gameObject);

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerInteractor] Server processed interaction with '{interactable.name}' " +
                $"id='{interactable.InteractableId}'. Success={success}",
                interactable);
        }
    }

    private string GetAuthoritativeAreaSceneName()
    {
        if (_areaStreamingController == null)
        {
            _areaStreamingController = GetComponent<PlayerAreaStreamingController>();
        }

        if (_areaStreamingController != null &&
            !string.IsNullOrWhiteSpace(_areaStreamingController.CurrentAreaSceneName))
        {
            return _areaStreamingController.CurrentAreaSceneName;
        }

        Scene scene = gameObject.scene;

        if (scene.IsValid() && !string.IsNullOrWhiteSpace(scene.name))
        {
            return scene.name;
        }

        return SceneManager.GetActiveScene().name;
    }
}
