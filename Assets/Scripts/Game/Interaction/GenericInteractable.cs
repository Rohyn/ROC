using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Generic interaction shell placed on any object the player can use.
///
/// This script does NOT hardcode any specific behavior like "rest" or "rotate".
/// Instead, it discovers attached InteractableAction components and executes them.
///
/// This version supports interaction consumption scope:
/// - None
/// - PerPlayer
/// - Global
///
/// IMPORTANT:
/// This is about AVAILABILITY after a successful interaction, not effect audience.
/// Future "buff all players in scene/range" behavior should be modeled at the action level,
/// not here.
///
/// IMPORTANT IMPLEMENTATION NOTE:
/// For in-scene placed NetworkObjects, NGO NetworkHide only despawns the NetworkObject on the client;
/// it does not destroy the underlying scene GameObject.
/// Because of that, this class explicitly manages local presentation/interactivity
/// when an interactable becomes consumed.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GenericInteractable : NetworkBehaviour
{
    [Header("Interaction")]
    [SerializeField] private string interactionPrompt = "Interact";
    [SerializeField] private float interactionRange = 2.5f;
    [SerializeField] private bool isEnabled = true;

    [Header("Selection / Targeting")]
    [SerializeField] private Transform interactionFocusPoint;
    [SerializeField] private Collider interactionTargetCollider;
    [SerializeField] private float selectionPriorityBonus = 0f;

    [Header("Consumption")]
    [Tooltip("If true, a successful interaction will consume this interactable according to the selected mode.")]
    [SerializeField] private bool consumeOnSuccessfulInteraction = false;

    [Tooltip("Controls who loses access to this interactable after successful use.")]
    [SerializeField] private InteractionConsumptionMode consumptionMode = InteractionConsumptionMode.None;

    [Tooltip("If true, local renderers/colliders will be disabled when this interactable is consumed for the local player.")]
    [SerializeField] private bool hidePresentationWhenConsumed = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly List<InteractableAction> _actions = new();
    private readonly List<InteractableAvailabilityRules> _availabilityRules = new();

    // ---------------------------------------------------------------------
    // Replicated consumption state
    // ---------------------------------------------------------------------

    /// <summary>
    /// True when the object is globally consumed for everyone.
    /// </summary>
    private readonly NetworkVariable<bool> _globallyConsumed =
        new NetworkVariable<bool>(false);

    /// <summary>
    /// Client IDs for which this interactable has been consumed.
    /// This is the first-pass per-player consumed state.
    /// </summary>
    private readonly NetworkList<ulong> _consumedByClientIds = new();

    // ---------------------------------------------------------------------
    // Local cached presentation state
    // ---------------------------------------------------------------------

    private Renderer[] _cachedRenderers;
    private Collider[] _cachedColliders;

    /// <summary>
    /// Local cached value indicating whether this interactable should be considered consumed
    /// for THIS client.
    /// </summary>
    private bool _locallyConsumedForThisClient;

    public string InteractionPrompt => interactionPrompt;
    public float InteractionRange => interactionRange;
    public bool IsEnabled => isEnabled;
    public float SelectionPriorityBonus => selectionPriorityBonus;
    public Collider InteractionTargetCollider => interactionTargetCollider;

    public Vector3 InteractionFocusPosition =>
        interactionFocusPoint != null ? interactionFocusPoint.position : transform.position;

    private void Awake()
    {
        CacheActions();
        CacheAvailabilityRules();
        CachePresentationComponents();
    }

    public override void OnNetworkSpawn()
    {
        _globallyConsumed.OnValueChanged += HandleGlobalConsumedChanged;
        _consumedByClientIds.OnListChanged += HandleConsumedByClientIdsChanged;

        RefreshLocalConsumedStateAndPresentation();
    }

    public override void OnNetworkDespawn()
    {
        _globallyConsumed.OnValueChanged -= HandleGlobalConsumedChanged;
        _consumedByClientIds.OnListChanged -= HandleConsumedByClientIdsChanged;
    }

    private void CacheActions()
    {
        _actions.Clear();

        InteractableAction[] actions = GetComponents<InteractableAction>();
        _actions.AddRange(actions);

        _actions.Sort((a, b) => a.ExecutionOrder.CompareTo(b.ExecutionOrder));
    }

    private void CachePresentationComponents()
    {
        _cachedRenderers = GetComponentsInChildren<Renderer>(true);
        _cachedColliders = GetComponentsInChildren<Collider>(true);
    }
    private void CacheAvailabilityRules()
    {
        _availabilityRules.Clear();

        InteractableAvailabilityRules[] rules = GetComponents<InteractableAvailabilityRules>();
        _availabilityRules.AddRange(rules);
    }

    private void OnValidate()
    {
        if (interactionRange < 0.1f)
        {
            interactionRange = 0.1f;
        }
    }

    public Vector3 GetInteractionEvaluationPoint(Vector3 origin)
    {
        if (interactionTargetCollider != null)
        {
            return interactionTargetCollider.ClosestPoint(origin);
        }

        return InteractionFocusPosition;
    }

    public bool CanInteract(GameObject interactorObject)
    {
        if (!isEnabled)
        {
            return false;
        }

        if (interactorObject == null)
        {
            return false;
        }

        // -------------------------------------------------------------
        // Consumption checks
        // -------------------------------------------------------------

        if (_globallyConsumed.Value)
        {
            return false;
        }

        NetworkObject interactorNetworkObject = interactorObject.GetComponent<NetworkObject>();
        if (interactorNetworkObject != null)
        {
            ulong clientId = interactorNetworkObject.OwnerClientId;

            for (int i = 0; i < _consumedByClientIds.Count; i++)
            {
                if (_consumedByClientIds[i] == clientId)
                {
                    return false;
                }
            }
        }

        // Also respect this client's local consumed state so prompt/selection shut off immediately.
        if (!IsServer && _locallyConsumedForThisClient)
        {
            return false;
        }

        InteractionContext context = new InteractionContext(interactorObject);

        for (int i = 0; i < _availabilityRules.Count; i++)
        {
            InteractableAvailabilityRules rules = _availabilityRules[i];
            if (rules == null)
            {
                continue;
            }

            if (!rules.CanInteract(context))
            {
                return false;
            }
        }

        // -------------------------------------------------------------
        // Range check
        // -------------------------------------------------------------
        Vector3 interactorPosition = interactorObject.transform.position;
        Vector3 evaluationPoint = GetInteractionEvaluationPoint(interactorPosition);

        float distance = Vector3.Distance(interactorPosition, evaluationPoint);
        return distance <= interactionRange;
    }

    public bool TryInteract(GameObject interactorObject)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[GenericInteractable] TryInteract should be executed on the server.", this);
            return false;
        }

        if (!CanInteract(interactorObject))
        {
            return false;
        }

        InteractionContext context = new InteractionContext(interactorObject);

        bool executedAtLeastOneAction = false;

        for (int i = 0; i < _actions.Count; i++)
        {
            InteractableAction action = _actions[i];
            if (action == null)
            {
                continue;
            }

            if (!action.CanExecute(context))
            {
                continue;
            }

            Debug.Log($"[GenericInteractable] Executing action '{action.GetType().Name}' on '{name}'.");

            action.Execute(context);
            executedAtLeastOneAction = true;

            if (context.StopFurtherActions)
            {
                break;
            }
        }

        if (executedAtLeastOneAction && consumeOnSuccessfulInteraction)
        {
            ApplyConsumption(context);
        }

        return executedAtLeastOneAction;
    }

    private void ApplyConsumption(InteractionContext context)
    {
        switch (consumptionMode)
        {
            case InteractionConsumptionMode.None:
                return;

            case InteractionConsumptionMode.PerPlayer:
            {
                if (!context.HasInteractorClientId)
                {
                    return;
                }

                MarkConsumedForClient(context.InteractorClientId);
                return;
            }

            case InteractionConsumptionMode.Global:
            {
                MarkConsumedGlobally();
                return;
            }
        }
    }

    private void MarkConsumedForClient(ulong clientId)
    {
        // Prevent duplicates.
        for (int i = 0; i < _consumedByClientIds.Count; i++)
        {
            if (_consumedByClientIds[i] == clientId)
            {
                return;
            }
        }

        _consumedByClientIds.Add(clientId);

        if (verboseLogging)
        {
            Debug.Log($"[GenericInteractable] Marked '{name}' consumed for client {clientId}.", this);
        }

        // If this is also the host's local client, refresh immediately on host side.
        RefreshLocalConsumedStateAndPresentation();
    }

    private void MarkConsumedGlobally()
    {
        if (_globallyConsumed.Value)
        {
            return;
        }

        _globallyConsumed.Value = true;

        if (verboseLogging)
        {
            Debug.Log($"[GenericInteractable] Marked '{name}' globally consumed.", this);
        }

        RefreshLocalConsumedStateAndPresentation();
    }

    private void HandleGlobalConsumedChanged(bool previousValue, bool newValue)
    {
        RefreshLocalConsumedStateAndPresentation();
    }

    private void HandleConsumedByClientIdsChanged(NetworkListEvent<ulong> changeEvent)
    {
        RefreshLocalConsumedStateAndPresentation();
    }

    /// <summary>
    /// Recomputes whether this interactable is consumed for the local client and updates local visuals/colliders.
    /// </summary>
    private void RefreshLocalConsumedStateAndPresentation()
    {
        bool isConsumedForLocalClient = _globallyConsumed.Value;

        if (!isConsumedForLocalClient && NetworkManager != null)
        {
            ulong localClientId = NetworkManager.LocalClientId;

            for (int i = 0; i < _consumedByClientIds.Count; i++)
            {
                if (_consumedByClientIds[i] == localClientId)
                {
                    isConsumedForLocalClient = true;
                    break;
                }
            }
        }

        _locallyConsumedForThisClient = isConsumedForLocalClient;

        if (hidePresentationWhenConsumed)
        {
            ApplyLocalPresentationState(!_locallyConsumedForThisClient);
        }
    }

    /// <summary>
    /// Enables or disables local renderers/colliders so consumed pickups actually disappear
    /// and stop being targetable on the local client.
    /// </summary>
    private void ApplyLocalPresentationState(bool visibleAndInteractive)
    {
        if (_cachedRenderers == null || _cachedColliders == null)
        {
            CachePresentationComponents();
        }

        if (_cachedRenderers != null)
        {
            for (int i = 0; i < _cachedRenderers.Length; i++)
            {
                Renderer rendererComponent = _cachedRenderers[i];
                if (rendererComponent == null)
                {
                    continue;
                }

                rendererComponent.enabled = visibleAndInteractive;
            }
        }

        if (_cachedColliders != null)
        {
            for (int i = 0; i < _cachedColliders.Length; i++)
            {
                Collider colliderComponent = _cachedColliders[i];
                if (colliderComponent == null)
                {
                    continue;
                }

                // Do not disable the NetworkObject itself; just the colliders.
                colliderComponent.enabled = visibleAndInteractive;
            }
        }
    }
}