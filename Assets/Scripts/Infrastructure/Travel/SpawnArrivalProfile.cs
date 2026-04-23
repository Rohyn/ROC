using UnityEngine;
using ROC.Statuses;

/// <summary>
/// Optional arrival behavior attached to or referenced by a SpawnPoint.
///
/// PURPOSE:
/// - Allow a spawn point to do more than just place the player at its own transform.
/// - Support cases like:
///   - spawn directly into a bed/chair anchor
///   - start anchored/restrained
///   - start with a status such as Resting
///
/// DESIGN:
/// - This component is data + simple application logic.
/// - ServerTravelManager remains the system that decides WHEN a player is spawned.
/// - This profile decides HOW the player should arrive once that spawn occurs.
/// </summary>
[DisallowMultipleComponent]
public class SpawnArrivalProfile : MonoBehaviour
{
    [Header("Initial Spawn Transform Override")]
    [Tooltip("If true, the player's initial spawned transform will use the arrival anchor instead of the SpawnPoint transform.")]
    [SerializeField] private bool overrideSpawnTransformWithArrivalAnchor = true;

    [Tooltip("Optional anchor to use for initial placement. For the intro bed case, assign the bed's RestAnchor.")]
    [SerializeField] private Transform arrivalAnchor;

    [Tooltip("If true, the player's initial spawn rotation will match the arrival anchor rotation.")]
    [SerializeField] private bool useArrivalAnchorRotation = true;

    [Header("Anchor State")]
    [Tooltip("If true, PlayerAnchorState will be initialized when the player arrives.")]
    [SerializeField] private bool setAnchorStateOnArrival = true;

    [Tooltip("Optional explicit owner object for the anchor relationship. For example, the Bed GameObject.")]
    [SerializeField] private GameObject anchorOwnerOverride;

    [Tooltip("Optional exit anchor used when the player is released from the anchored state.")]
    [SerializeField] private Transform exitAnchor;

    [Header("Status")]
    [Tooltip("If true, the specified status will be applied when the player arrives.")]
    [SerializeField] private bool applyStatusOnArrival = true;

    [Tooltip("Status to apply on arrival. For the intro bed case, assign the Resting status.")]
    [SerializeField] private StatusDefinition statusToApplyOnArrival;

    [Header("Debug")]
    [Tooltip("If true, arrival application will be logged.")]
    [SerializeField] private bool verboseLogging = true;

    /// <summary>
    /// Returns true if this profile should override the player's initial spawn transform.
    /// </summary>
    public bool HasSpawnTransformOverride =>
        overrideSpawnTransformWithArrivalAnchor &&
        arrivalAnchor != null;

    /// <summary>
    /// Returns the arrival anchor, if any.
    /// </summary>
    public Transform ArrivalAnchor => arrivalAnchor;

    /// <summary>
    /// Calculates the initial spawn pose that should be used for the player.
    /// If no override is configured, the provided defaults are returned unchanged.
    /// </summary>
    public void GetInitialSpawnPose(
        Vector3 defaultPosition,
        Quaternion defaultRotation,
        out Vector3 finalPosition,
        out Quaternion finalRotation)
    {
        finalPosition = defaultPosition;
        finalRotation = defaultRotation;

        if (!HasSpawnTransformOverride)
        {
            return;
        }

        finalPosition = arrivalAnchor.position;

        if (useArrivalAnchorRotation)
        {
            finalRotation = arrivalAnchor.rotation;
        }
    }

    /// <summary>
    /// Applies post-spawn arrival state to the player.
    ///
    /// IMPORTANT:
    /// This should be called on the server after the player's NetworkObject has been spawned.
    /// That ensures server-authoritative systems like StatusManager are active and valid.
    /// </summary>
    public void ApplyToPlayer(GameObject playerObject)
    {
        if (playerObject == null)
        {
            return;
        }

        // -------------------------------------------------------------
        // Anchor state
        // -------------------------------------------------------------
        if (setAnchorStateOnArrival && arrivalAnchor != null)
        {
            PlayerAnchorState anchorState = playerObject.GetComponent<PlayerAnchorState>();
            if (anchorState != null)
            {
                GameObject anchorOwner =
                    anchorOwnerOverride != null
                    ? anchorOwnerOverride
                    : arrivalAnchor.gameObject;

                anchorState.SetAnchor(anchorOwner, arrivalAnchor, exitAnchor);

                if (verboseLogging)
                {
                    string ownerName = anchorOwner != null ? anchorOwner.name : "null";
                    string exitName = exitAnchor != null ? exitAnchor.name : "null";
                    Debug.Log($"[SpawnArrivalProfile] Applied anchor state. Owner={ownerName}, ArrivalAnchor={arrivalAnchor.name}, ExitAnchor={exitName}");
                }
            }
            else if (verboseLogging)
            {
                Debug.LogWarning("[SpawnArrivalProfile] Player has no PlayerAnchorState component.");
            }
        }

        // -------------------------------------------------------------
        // Status
        // -------------------------------------------------------------
        if (applyStatusOnArrival && statusToApplyOnArrival != null)
        {
            StatusManager statusManager = playerObject.GetComponent<StatusManager>();
            if (statusManager != null)
            {
                statusManager.ApplyStatus(statusToApplyOnArrival);

                if (verboseLogging)
                {
                    Debug.Log($"[SpawnArrivalProfile] Applied arrival status '{statusToApplyOnArrival.DisplayName}'.");
                }
            }
            else if (verboseLogging)
            {
                Debug.LogWarning("[SpawnArrivalProfile] Player has no StatusManager component.");
            }
        }
    }
}