using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Generic interaction shell placed on any object the player can use.
///
/// This script does NOT hardcode any specific behavior like "rest" or "rotate".
/// Instead, it discovers attached InteractableAction components and executes them.
///
/// This version also supports interaction consumption scope:
/// - None
/// - PerPlayer
/// - Global
///
/// IMPORTANT:
/// This is about AVAILABILITY after a successful interaction, not effect audience.
/// Future "buff all players in scene/range" behavior should be modeled at the action level,
/// not here.
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

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly List<InteractableAction> _actions = new();

    // Server-only state
    private bool _globallyConsumed;
    private readonly HashSet<ulong> _consumedByClientIds = new();

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
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkObject.CheckObjectVisibility += CheckVisibility;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkObject.CheckObjectVisibility -= CheckVisibility;
        }
    }

    private void CacheActions()
    {
        _actions.Clear();

        InteractableAction[] actions = GetComponents<InteractableAction>();
        _actions.AddRange(actions);

        _actions.Sort((a, b) => a.ExecutionOrder.CompareTo(b.ExecutionOrder));
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

        // Server can enforce consumption rules authoritatively.
        if (IsServer)
        {
            if (_globallyConsumed)
            {
                return false;
            }

            NetworkObject interactorNetworkObject = interactorObject.GetComponent<NetworkObject>();
            if (interactorNetworkObject != null)
            {
                ulong clientId = interactorNetworkObject.OwnerClientId;

                if (_consumedByClientIds.Contains(clientId))
                {
                    return false;
                }
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
        if (!_consumedByClientIds.Add(clientId))
        {
            return;
        }

        if (verboseLogging)
        {
            Debug.Log($"[GenericInteractable] Marked '{name}' consumed for client {clientId}.", this);
        }

        if (NetworkObject.IsSpawned && NetworkObject.IsNetworkVisibleTo(clientId))
        {
            NetworkObject.NetworkHide(clientId);
        }
    }

    private void MarkConsumedGlobally()
    {
        if (_globallyConsumed)
        {
            return;
        }

        _globallyConsumed = true;

        if (verboseLogging)
        {
            Debug.Log($"[GenericInteractable] Marked '{name}' globally consumed.", this);
        }

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (NetworkObject.IsSpawned && NetworkObject.IsNetworkVisibleTo(clientId))
            {
                NetworkObject.NetworkHide(clientId);
            }
        }
    }

    /// <summary>
    /// Visibility callback used for late-joiners and initial spawn visibility.
    /// </summary>
    private bool CheckVisibility(ulong clientId)
    {
        if (_globallyConsumed)
        {
            return false;
        }

        if (_consumedByClientIds.Contains(clientId))
        {
            return false;
        }

        return true;
    }
}