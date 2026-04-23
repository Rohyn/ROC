using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Handles the owning player's interaction input.
///
/// This version does not perform broad search or selection itself.
/// Instead, it relies on PlayerInteractionSelector to provide the current best target.
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

    private void Start()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }
    }

    private void Update()
    {
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
            TryInteractCurrentTarget();
        }
    }

    private void TryInteractCurrentTarget()
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

        bool success = target.TryInteract(gameObject);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerInteractor] Tried interaction with '{target.name}'. Success={success}");
        }
    }
}