using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Generic interaction shell placed on any object the player can use.
///
/// This script does NOT hardcode any specific behavior like "rest" or "rotate".
/// Instead, it discovers attached InteractableAction components and executes them.
///
/// This version supports:
/// - interaction consumption scope
/// - optional target collider for wide/low objects
/// - ANY NUMBER of interaction focus points
///
/// IMPORTANT:
/// - interactionTargetCollider is still the best choice for distance/range checks
/// - interactionFocusPoints are used for facing checks, LOS, and prompt placement
/// - when multiple focus points exist, the nearest one to the querying origin is used
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GenericInteractable : NetworkBehaviour
{
    [Header("Interaction")]
    [SerializeField] private string interactionPrompt = "Interact";
    [SerializeField] private float interactionRange = 2.5f;
    [SerializeField] private bool isEnabled = true;
    [Tooltip("Stable identifier used by quests and other systems when this object is interacted with.")]
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

    private readonly NetworkVariable<bool> _globallyConsumed =
        new NetworkVariable<bool>(false);

    private readonly NetworkList<ulong> _consumedByClientIds = new();

    private Renderer[] _cachedRenderers;
    private Collider[] _cachedColliders;
    private bool _locallyConsumedForThisClient;

    public string InteractionPrompt => interactionPrompt;
    public float InteractionRange => interactionRange;
    public bool IsEnabled => isEnabled;
    public float SelectionPriorityBonus => selectionPriorityBonus;
    public Collider InteractionTargetCollider => interactionTargetCollider;
    public string InteractableId => !string.IsNullOrWhiteSpace(interactableId) ? interactableId : name;

    /// <summary>
    /// Backward-compatible convenience property.
    /// Uses the nearest focus point to this object's root position.
    /// This is mainly a fallback; callers should prefer GetBestInteractionFocusPosition(referencePosition).
    /// </summary>
    public Vector3 InteractionFocusPosition => GetBestInteractionFocusPosition(transform.position);

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

    private void OnValidate()
    {
        if (interactionRange < 0.1f)
        {
            interactionRange = 0.1f;
        }
    }

    /// <summary>
    /// Returns the best point on or for this interactable to evaluate range/distance against.
    ///
    /// Priority:
    /// 1. ClosestPoint on the configured target collider
    /// 2. Best interaction focus point relative to origin
    /// 3. Interactable root transform position
    /// </summary>
    public Vector3 GetInteractionEvaluationPoint(Vector3 origin)
    {
        if (interactionTargetCollider != null)
        {
            return interactionTargetCollider.ClosestPoint(origin);
        }

        return GetBestInteractionFocusPosition(origin);
    }

    /// <summary>
    /// Returns the nearest valid interaction focus point relative to the provided reference position.
    ///
    /// If no focus points are assigned, falls back to the interactable root transform position.
    /// </summary>
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

        // -------------------------------------------------------------
        // Conversation lockout
        // -------------------------------------------------------------
        // While the player is already in a conversation, all world interaction
        // should be suppressed. This prevents prompts/selection from feeling weird
        // and avoids repeated E presses on nearby objects or the same NPC.
        PlayerConversationState conversationState = interactorObject.GetComponent<PlayerConversationState>();
        if (conversationState != null && conversationState.IsConversationOpen)
        {
            return false;
        }
        // -------------------------------------------------------------
        // Area transfer lockout
        // -------------------------------------------------------------
        // While the player is in the middle of an area transfer, suppress all
        // world interactions and prompts.
        PlayerAreaStreamingController areaStreaming =
            interactorObject.GetComponent<PlayerAreaStreamingController>();

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

        if (executedAtLeastOneAction)
        {
            // Emit a normalized interaction event for quests.
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
}