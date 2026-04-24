using UnityEngine;

/// <summary>
/// Streams the interacting player into a destination area scene and teleports them
/// to a spawn point within that area.
///
/// Intended first use:
/// - Intro infirmary door -> DKeepCenter:intro_arrival
///
/// This action assumes:
/// - it executes on the server via GenericInteractable.TryInteract
/// - the player has PlayerAreaStreamingController on their prefab
///
/// Optional:
/// - require a rotation state to be open before transfer is allowed
/// </summary>
public class TransferPlayerAction : InteractableAction
{
    [Header("Destination")]
    [SerializeField] private TravelDestination destination;

    [Header("Optional Gate")]
    [Tooltip("If assigned, transfer only succeeds if this rotation state is currently open for the player.")]
    [SerializeField] private InteractableRotationState requiredOpenRotationState;

    [Header("Flow Control")]
    [SerializeField] private bool stopFurtherActionsOnSuccess = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null || !context.HasInteractorClientId)
        {
            return false;
        }

        if (!destination.IsValid)
        {
            return false;
        }

        PlayerAreaStreamingController areaStreaming =
            context.InteractorObject.GetComponent<PlayerAreaStreamingController>();

        if (areaStreaming == null || areaStreaming.IsAreaTransferInProgress)
        {
            return false;
        }

        if (requiredOpenRotationState != null)
        {
            bool isOpenForThisPlayer =
                requiredOpenRotationState.GetOpenStateForClientId(context.InteractorClientId);

            if (!isOpenForThisPlayer)
            {
                return false;
            }
        }

        return true;
    }

    public override void Execute(InteractionContext context)
    {
        if (context == null || context.InteractorObject == null)
        {
            return;
        }

        PlayerAreaStreamingController areaStreaming =
            context.InteractorObject.GetComponent<PlayerAreaStreamingController>();

        if (areaStreaming == null)
        {
            return;
        }

        bool started = areaStreaming.BeginAreaTransfer(destination);

        if (verboseLogging)
        {
            Debug.Log(started
                ? $"[TransferPlayerAction] Began area transfer to {destination}."
                : $"[TransferPlayerAction] Failed to begin area transfer to {destination}.");
        }

        if (started && stopFurtherActionsOnSuccess)
        {
            context.StopFurtherActions = true;
        }
    }
}