using UnityEngine;
using ROC.Statuses;

/// <summary>
/// Context passed into an interaction action.
///
/// This keeps action code generic:
/// actions do not need to know specific player script names in advance.
/// They can query what they need from the interacting object.
///
/// Later, this can be expanded with:
/// - inventory
/// - skill manager
/// - faction/reputation
/// - server authority info
/// - source item/tool used for interaction
/// </summary>
public sealed class InteractionContext
{
    /// <summary>
    /// The root GameObject of the interacting entity, usually the player.
    /// </summary>
    public GameObject InteractorObject { get; }

    /// <summary>
    /// Convenience access to the interactor's transform.
    /// </summary>
    public Transform InteractorTransform => InteractorObject != null ? InteractorObject.transform : null;

    /// <summary>
    /// Cached StatusManager on the interactor, if present.
    /// </summary>
    public StatusManager InteractorStatusManager { get; }

    /// <summary>
    /// Cached CharacterController on the interactor, if present.
    /// </summary>
    public CharacterController InteractorCharacterController { get; }

    /// <summary>
    /// Cached PlayerAnchorState on the interactor, if present.
    /// </summary>
    public PlayerAnchorState InteractorAnchorState { get; }

    /// <summary>
    /// If an action sets this to true, GenericInteractable should stop executing
    /// any remaining actions in the chain.
    /// </summary>
    public bool StopFurtherActions { get; set; }

    public InteractionContext(GameObject interactorObject)
    {
        InteractorObject = interactorObject;

        if (InteractorObject != null)
        {
            InteractorStatusManager = InteractorObject.GetComponent<StatusManager>();
            InteractorCharacterController = InteractorObject.GetComponent<CharacterController>();
            InteractorAnchorState = InteractorObject.GetComponent<PlayerAnchorState>();
        }

        StopFurtherActions = false;
    }
}