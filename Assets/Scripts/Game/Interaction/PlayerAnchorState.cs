using UnityEngine;

/// <summary>
/// Tracks whether the player is currently anchored to some interactable object,
/// along with where they should be released when that anchored state ends.
///
/// IMPORTANT:
/// This first version does NOT force the player to continuously follow an anchor.
/// It is intentionally lightweight:
/// - remembers the current anchor owner/object
/// - remembers the current anchor transform
/// - remembers an optional exit anchor
/// - provides a standard "release" behavior
///
/// This is enough for:
/// - beds
/// - chairs
/// - simple restraint points
///
/// Later, this can be extended to support:
/// - true tethering / attachment
/// - moving anchors
/// - occupancy rules
/// - animation pose rules
/// - camera overrides
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnchorState : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("If true, anchor state changes will be logged.")]
    [SerializeField] private bool verboseLogging = true;

    // Cached CharacterController on the player.
    // We disable/re-enable it when snapping to avoid controller conflicts.
    private CharacterController _characterController;

    /// <summary>
    /// The object that currently "owns" the anchor relationship.
    /// For example:
    /// - the bed
    /// - the chair
    /// - the chain point
    /// - the spider web
    /// </summary>
    public GameObject CurrentAnchorOwner { get; private set; }

    /// <summary>
    /// The transform the player is considered anchored to.
    /// This is the "main" anchor, such as a bed position or chair position.
    /// </summary>
    public Transform CurrentAnchor { get; private set; }

    /// <summary>
    /// Optional exit anchor used when releasing the player.
    /// Example:
    /// - stand-up position beside a bed
    /// - stand-up point beside a chair
    /// </summary>
    public Transform CurrentExitAnchor { get; private set; }

    /// <summary>
    /// Returns true if the player currently has an active anchor state.
    /// </summary>
    public bool IsAnchored => CurrentAnchorOwner != null || CurrentAnchor != null || CurrentExitAnchor != null;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
    }

    /// <summary>
    /// Sets the player's current anchor state.
    ///
    /// This does NOT move the player by itself.
    /// Spatial snapping should be handled separately by SnapToAnchorAction.
    /// </summary>
    public void SetAnchor(GameObject anchorOwner, Transform anchor, Transform exitAnchor)
    {
        CurrentAnchorOwner = anchorOwner;
        CurrentAnchor = anchor;
        CurrentExitAnchor = exitAnchor;

        if (verboseLogging)
        {
            string ownerName = CurrentAnchorOwner != null ? CurrentAnchorOwner.name : "null";
            string anchorName = CurrentAnchor != null ? CurrentAnchor.name : "null";
            string exitName = CurrentExitAnchor != null ? CurrentExitAnchor.name : "null";

            Debug.Log($"[PlayerAnchorState] Anchor set. Owner={ownerName}, Anchor={anchorName}, ExitAnchor={exitName}", this);
        }
    }

    /// <summary>
    /// Clears the current anchor state without moving the player.
    /// </summary>
    public void ClearAnchor()
    {
        if (verboseLogging && IsAnchored)
        {
            Debug.Log("[PlayerAnchorState] Anchor cleared without moving player.", this);
        }

        CurrentAnchorOwner = null;
        CurrentAnchor = null;
        CurrentExitAnchor = null;
    }

    /// <summary>
    /// Releases the player from the current anchor state.
    ///
    /// If an exit anchor is assigned, the player is moved there.
    /// If no exit anchor is assigned, this simply clears the anchor state.
    /// </summary>
    public void ReleaseToExitAnchor()
    {
        if (!IsAnchored)
        {
            return;
        }

        Transform exitAnchor = CurrentExitAnchor;

        // Clear state first so that, even if something goes wrong during movement,
        // the player is not considered stuck in the anchored state forever.
        CurrentAnchorOwner = null;
        CurrentAnchor = null;
        CurrentExitAnchor = null;

        // If there is no exit anchor, we are done.
        if (exitAnchor == null)
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerAnchorState] Released anchor state with no exit anchor.", this);
            }

            return;
        }

        bool shouldDisableController =
            _characterController != null &&
            _characterController.enabled;

        if (shouldDisableController)
        {
            _characterController.enabled = false;
        }

        transform.SetPositionAndRotation(exitAnchor.position, exitAnchor.rotation);

        if (shouldDisableController)
        {
            _characterController.enabled = true;
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerAnchorState] Released to exit anchor '{exitAnchor.name}'.", this);
        }
    }
}