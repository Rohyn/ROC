using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Generic interaction shell placed on any object the player can use.
///
/// This script does NOT hardcode any specific behavior like "rest" or "rotate".
/// Instead, it discovers attached InteractableAction components and executes them.
///
/// Supports:
/// - stable authored InteractableId lookup
/// - interaction consumption scope
/// - optional target collider for wide/low objects
/// - any number of interaction focus points
///
/// IMPORTANT:
/// - Static authored scene objects can be resolved by InteractableId.
/// - They do not need to be spawned NetworkObjects just to be interacted with.
/// - Networked consumption modes still require normal Netcode spawning to replicate.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GenericInteractable : NetworkBehaviour
{
    [Header("Interaction")]
    [SerializeField] private string interactionPrompt = "Interact";
    [SerializeField] private float interactionRange = 2.5f;
    [SerializeField] private bool isEnabled = true;

    [Tooltip("Stable identifier used by quests, interaction lookup, and other systems. Must be unique within the scene/area.")]
    [SerializeField] private string interactableId;

    [Header("Selection / Targeting")]
    [Tooltip("Optional focus points used for facing checks, LOS, and prompt placement. The nearest valid one is chosen at runtime.")]
    [SerializeField] private Transform[] interactionFocusPoints;

    [Tooltip("Optional collider used as the selection target volume. When assigned, range/distance checks use ClosestPoint on this collider.")]
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

    private readonly NetworkVariable<bool> _globallyConsumed = new(false);
    private readonly NetworkList<ulong> _consumedByClientIds = new();

    private Renderer[] _cachedRenderers;
    private Collider[] _cachedColliders;
    private bool _locallyConsumedForThisClient;

    public string InteractionPrompt => interactionPrompt;
    public float InteractionRange => interactionRange;
    public bool IsEnabled => isEnabled;
    public float SelectionPriorityBonus => selectionPriorityBonus;
    public Collider InteractionTargetCollider => interactionTargetCollider;

    public string InteractableId => !string.IsNullOrWhiteSpace(interactableId)
        ? interactableId
        : name;

    public Vector3 InteractionFocusPosition => GetBestInteractionFocusPosition(transform.position);

    private void Awake()
    {
        CacheActions();
        CacheAvailabilityRules();
        CachePresentationComponents();
    }

    private void OnEnable()
    {
        InteractableRegistry.Register(this);
    }

    private void OnDisable()
    {
        InteractableRegistry.Unregister(this);
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

    private void OnValidate()
    {
        if (interactionRange < 0.1f)
        {
            interactionRange = 0.1f;
        }
    }

    private void CacheActions()
    {
        _actions.Clear();

        InteractableAction[] actions = GetComponents<InteractableAction>();
        _actions.AddRange(actions);
        _actions.Sort((a, b) => a.ExecutionOrder.CompareTo(b.ExecutionOrder));
    }

    private void CacheAvailabilityRules()
    {
        _availabilityRules.Clear();

        InteractableAvailabilityRules[] rules = GetComponents<InteractableAvailabilityRules>();
        _availabilityRules.AddRange(rules);
    }

    private void CachePresentationComponents()
    {
        _cachedRenderers = GetComponentsInChildren<Renderer>(true);
        _cachedColliders = GetComponentsInChildren<Collider>(true);
    }

    public Vector3 GetInteractionEvaluationPoint(Vector3 origin)
    {
        if (interactionTargetCollider != null)
        {
            return interactionTargetCollider.ClosestPoint(origin);
        }

        return GetBestInteractionFocusPosition(origin);
    }

    public Vector3 GetBestInteractionFocusPosition(Vector3 referencePosition)
    {
        if (interactionFocusPoints == null || interactionFocusPoints.Length == 0)
        {
            return transform.position;
        }

        bool foundAny = false;
        float bestSqrDistance = float.PositiveInfinity;
        Vector3 bestPosition = transform.position;

        for (int i = 0; i < interactionFocusPoints.Length; i++)
        {
            Transform focus = interactionFocusPoints[i];

            if (focus == null)
            {
                continue;
            }

            float sqrDistance = (focus.position - referencePosition).sqrMagnitude;

            if (!foundAny || sqrDistance < bestSqrDistance)
            {
                foundAny = true;
                bestSqrDistance = sqrDistance;
                bestPosition = focus.position;
            }
        }

        return foundAny ? bestPosition : transform.position;
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

        PlayerConversationState conversationState = interactorObject.GetComponent<PlayerConversationState>();
        if (conversationState != null && conversationState.IsConversationOpen)
        {
            return false;
        }

        PlayerAreaStreamingController areaStreaming = interactorObject.GetComponent<PlayerAreaStreamingController>();
        if (areaStreaming != null && areaStreaming.IsAreaTransferInProgress)
        {
            return false;
        }

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

        if (!IsServerContext() && _locallyConsumedForThisClient)
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

        Vector3 interactorPosition = interactorObject.transform.position;
        Vector3 evaluationPoint = GetInteractionEvaluationPoint(interactorPosition);
        float distance = Vector3.Distance(interactorPosition, evaluationPoint);

        return distance <= interactionRange;
    }

    public bool TryInteract(GameObject interactorObject)
    {
        if (!IsServerContext())
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

            if (verboseLogging)
            {
                Debug.Log($"[GenericInteractable] Executing action '{action.GetType().Name}' on '{name}'.", this);
            }

            action.Execute(context);
            executedAtLeastOneAction = true;

            if (context.StopFurtherActions)
            {
                break;
            }
        }

        if (executedAtLeastOneAction)
        {
            QuestEventUtility.EmitToPlayer(
                context.InteractorObject,
                GameplayEventData.CreateInteractedWithObjectEvent(InteractableId));
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

                if (!IsSpawned)
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning(
                            $"[GenericInteractable] '{name}' uses PerPlayer consumption but is not a spawned NetworkObject. " +
                            "Use persistent progress flags for durable per-character hiding.",
                            this);
                    }

                    return;
                }

                MarkConsumedForClient(context.InteractorClientId);
                return;
            }

            case InteractionConsumptionMode.Global:
            {
                if (!IsSpawned)
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning(
                            $"[GenericInteractable] '{name}' uses Global consumption but is not a spawned NetworkObject. " +
                            "Use a networked world-state component or spawn this object through Netcode.",
                            this);
                    }

                    return;
                }

                MarkConsumedGlobally();
                return;
            }
        }
    }

    private void MarkConsumedForClient(ulong clientId)
    {
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

    private void RefreshLocalConsumedStateAndPresentation()
    {
        bool isConsumedForLocalClient = _globallyConsumed.Value;

        if (!isConsumedForLocalClient)
        {
            NetworkManager networkManager = NetworkManager != null
                ? NetworkManager
                : NetworkManager.Singleton;

            if (networkManager != null)
            {
                ulong localClientId = networkManager.LocalClientId;

                for (int i = 0; i < _consumedByClientIds.Count; i++)
                {
                    if (_consumedByClientIds[i] == localClientId)
                    {
                        isConsumedForLocalClient = true;
                        break;
                    }
                }
            }
        }

        _locallyConsumedForThisClient = isConsumedForLocalClient;

        if (hidePresentationWhenConsumed)
        {
            ApplyLocalPresentationState(!_locallyConsumedForThisClient);
        }
    }

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

                colliderComponent.enabled = visibleAndInteractive;
            }
        }
    }

    private bool IsServerContext()
    {
        if (IsServer)
        {
            return true;
        }

        NetworkManager networkManager = NetworkManager != null
            ? NetworkManager
            : NetworkManager.Singleton;

        return networkManager != null && networkManager.IsServer;
    }
}