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

    // Cached ordered actions on this object.
    private readonly List<InteractableAction> _actions = new();

    public string InteractionPrompt => interactionPrompt;
    public float InteractionRange => interactionRange;
    public bool IsEnabled => isEnabled;

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
    /// Returns true if the specified interactor is close enough and this interactable is enabled.
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

        float distance = Vector3.Distance(interactorObject.transform.position, transform.position);
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