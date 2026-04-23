using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic interaction shell placed on any object the player can use.
///
/// This script does NOT hardcode any specific behavior like "rest" or "rotate".
/// Instead, it discovers attached InteractableAction components and executes them.
///
/// This is the compositional core of the interaction system.
/// </summary>
public class GenericInteractable : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("Text that can later be shown in the UI, for example 'Rest' or 'Open Door'.")]
    [SerializeField] private string interactionPrompt = "Interact";

    [Tooltip("Maximum interaction distance measured from the player's transform to this interactable.")]
    [SerializeField] private float interactionRange = 2.5f;

    [Tooltip("If false, this object ignores interaction requests.")]
    [SerializeField] private bool isEnabled = true;

    [Tooltip("Optional point used for scoring / line-of-sight / prompt targeting. If left empty, the interactable root transform is used.")]
    [SerializeField] private Transform interactionFocusPoint;

    private readonly List<InteractableAction> _actions = new();

    public string InteractionPrompt => interactionPrompt;
    public float InteractionRange => interactionRange;
    public bool IsEnabled => isEnabled;

    /// <summary>
    /// World-space point the interaction selector should use when evaluating this object.
    /// </summary>
    public Vector3 InteractionFocusPosition =>
        interactionFocusPoint != null ? interactionFocusPoint.position : transform.position;

    private void Awake()
    {
        CacheActions();
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

        float distance = Vector3.Distance(interactorObject.transform.position, transform.position);
        return distance <= interactionRange;
    }

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