using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic interaction shell placed on any object the player can use.
///
/// This script does NOT hardcode any specific behavior like "rest" or "rotate".
/// Instead, it discovers attached InteractableAction components and executes them.
///
/// This is the compositional core of the interaction system.
///
/// NEW IN THIS VERSION:
/// - Optional interaction focus point for simple interactables
/// - Optional interaction target collider for "wide" interactables like beds, tables, counters, etc.
/// - Selection priority bonus for later use
/// - Range checks can evaluate against the best available interaction point, not just the object root
/// </summary>
public class GenericInteractable : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("Text that can later be shown in the UI, for example 'Rest' or 'Open Door'.")]
    [SerializeField] private string interactionPrompt = "Interact";

    [Tooltip("Maximum interaction distance measured from the player to this interactable.")]
    [SerializeField] private float interactionRange = 2.5f;

    [Tooltip("If false, this object ignores interaction requests.")]
    [SerializeField] private bool isEnabled = true;

    [Header("Selection / Targeting")]
    [Tooltip("Optional point used for scoring and prompts when no target collider is provided.")]
    [SerializeField] private Transform interactionFocusPoint;

    [Tooltip("Optional collider used as the selection target volume. When assigned, the selector can use ClosestPoint on this collider, which is ideal for large or low objects like beds.")]
    [SerializeField] private Collider interactionTargetCollider;

    [Tooltip("Optional selection bias. Higher values make this interactable slightly easier to keep selected compared with other nearby options.")]
    [SerializeField] private float selectionPriorityBonus = 0f;

    // Cached ordered actions on this object.
    private readonly List<InteractableAction> _actions = new();

    public string InteractionPrompt => interactionPrompt;
    public float InteractionRange => interactionRange;
    public bool IsEnabled => isEnabled;
    public float SelectionPriorityBonus => selectionPriorityBonus;
    public Collider InteractionTargetCollider => interactionTargetCollider;

    /// <summary>
    /// Simple fallback focus position used if no target collider is assigned.
    /// </summary>
    public Vector3 InteractionFocusPosition =>
        interactionFocusPoint != null ? interactionFocusPoint.position : transform.position;

    private void Awake()
    {
        CacheActions();
    }

    /// <summary>
    /// Rebuilds and sorts the action list.
    /// This is useful if you add/remove action components during editing.
    /// </summary>
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

    /// <summary>
    /// Returns the best point on or for this interactable to evaluate against from a given origin.
    ///
    /// Priority:
    /// 1. ClosestPoint on the configured target collider
    /// 2. Explicit interaction focus point
    /// 3. Interactable root transform position
    /// </summary>
    public Vector3 GetInteractionEvaluationPoint(Vector3 origin)
    {
        if (interactionTargetCollider != null)
        {
            return interactionTargetCollider.ClosestPoint(origin);
        }

        return InteractionFocusPosition;
    }

    /// <summary>
    /// Returns true if the specified interactor is close enough and this interactable is enabled.
    ///
    /// IMPORTANT:
    /// This version checks range against the best available interaction evaluation point,
    /// not just the interactable's root transform.
    /// That makes large objects like beds behave much more naturally.
    /// </summary>
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

        Vector3 interactorPosition = interactorObject.transform.position;
        Vector3 evaluationPoint = GetInteractionEvaluationPoint(interactorPosition);

        float distance = Vector3.Distance(interactorPosition, evaluationPoint);
        return distance <= interactionRange;
    }

    /// <summary>
    /// Attempts to run this interactable for the given interactor.
    ///
    /// For now:
    /// - validates distance
    /// - builds an interaction context
    /// - executes all attached actions whose CanExecute passes
    /// - stops early if an action sets context.StopFurtherActions
    /// </summary>
    public bool TryInteract(GameObject interactorObject)
    {
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

        return executedAtLeastOneAction;
    }
}