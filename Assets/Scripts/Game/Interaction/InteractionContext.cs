using UnityEngine;
using Unity.Netcode;
using ROC.Statuses;
using ROC.Inventory;

/// <summary>
/// Context passed into an interaction action.
///
/// This keeps action code generic:
/// actions do not need to know specific player script names in advance.
/// They can query what they need from the interacting object.
///
/// Later, this can be expanded with:
/// - skill manager
/// - faction/reputation
/// - source item/tool used for interaction
/// </summary>
public sealed class InteractionContext
{
    public GameObject InteractorObject { get; }
    public Transform InteractorTransform => InteractorObject != null ? InteractorObject.transform : null;

    public NetworkObject InteractorNetworkObject { get; }
    public bool HasInteractorClientId => InteractorNetworkObject != null;
    public ulong InteractorClientId => InteractorNetworkObject != null ? InteractorNetworkObject.OwnerClientId : 0;

    public StatusManager InteractorStatusManager { get; }
    public CharacterController InteractorCharacterController { get; }
    public PlayerAnchorState InteractorAnchorState { get; }
    public PlayerInventory InteractorInventory { get; }

    public bool StopFurtherActions { get; set; }

    public InteractionContext(GameObject interactorObject)
    {
        InteractorObject = interactorObject;

        if (InteractorObject != null)
        {
            InteractorNetworkObject = InteractorObject.GetComponent<NetworkObject>();
            InteractorStatusManager = InteractorObject.GetComponent<StatusManager>();
            InteractorCharacterController = InteractorObject.GetComponent<CharacterController>();
            InteractorAnchorState = InteractorObject.GetComponent<PlayerAnchorState>();
            InteractorInventory = InteractorObject.GetComponent<PlayerInventory>();
        }

        StopFurtherActions = false;
    }
}