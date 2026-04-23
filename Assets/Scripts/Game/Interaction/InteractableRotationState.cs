using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Stateful replicated rotation controller for an interactable object.
///
/// PURPOSE:
/// - Handle "open / close / toggle" rotation as persistent state
/// - Support either:
///   - Global state (all players see the same result)
///   - Per-player state (each player can have their own local view of open/closed)
///
/// IMPORTANT:
/// - This is a state component, not an interaction action.
/// - Attach this to the same GameObject that has the NetworkObject / GenericInteractable.
/// - For best results, assign a child transform whose local pivot is already at the hinge.
///   Example:
///   - DoorRoot (NetworkObject, GenericInteractable, InteractableRotationState)
///     - DoorHinge (assign as rotatingTransform)
///       - DoorMesh
///
/// This first version is intentionally binary:
/// - closed
/// - open
///
/// That is enough for:
/// - doors
/// - chests
/// - shutters
///
/// Later, you can extend this toward more advanced puzzle rotation states if needed.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class InteractableRotationState : NetworkBehaviour
{
    public enum RotationScope
    {
        Global = 0,
        PerPlayer = 1
    }

    public enum RotationCommand
    {
        Toggle = 0,
        OpenOnly = 1,
        CloseOnly = 2
    }

    public enum RotationAxis
    {
        LocalX = 0,
        LocalY = 1,
        LocalZ = 2,
        CustomLocal = 3
    }

    [Header("Target")]
    [Tooltip("Transform that actually rotates. Usually a hinge child object, not the root.")]
    [SerializeField] private Transform rotatingTransform;

    [Header("Rotation")]
    [Tooltip("Whether this rotation state is shared globally or tracked per-player.")]
    [SerializeField] private RotationScope rotationScope = RotationScope.Global;

    [Tooltip("Local axis used for opening/closing.")]
    [SerializeField] private RotationAxis rotationAxis = RotationAxis.LocalY;

    [Tooltip("Custom local axis if RotationAxis is CustomLocal.")]
    [SerializeField] private Vector3 customLocalAxis = Vector3.up;

    [Tooltip("Open angle in degrees. Positive or negative values control opening direction.")]
    [SerializeField] private float openAngleDegrees = 90f;

    [Tooltip("How quickly the object rotates toward the target angle.")]
    [SerializeField] private float rotationSpeedDegreesPerSecond = 180f;

    [Tooltip("If true, a globally-scoped object can begin already open when the network spawns.")]
    [SerializeField] private bool startOpen = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    // -------------------------------------------------------------
    // Replicated state
    // -------------------------------------------------------------

    // Shared open/closed state for globally replicated rotation.
    private readonly NetworkVariable<bool> _globalIsOpen = new(false);

    // Per-player open/closed membership for per-player rotation.
    private readonly NetworkList<ulong> _openForClientIds = new();

    // -------------------------------------------------------------
    // Local visual state
    // -------------------------------------------------------------

    private Quaternion _closedLocalRotation;
    private Quaternion _openLocalRotation;

    private void Awake()
    {
        if (rotatingTransform == null)
        {
            rotatingTransform = transform;
        }

        CacheClosedAndOpenRotations();
    }

    private void OnValidate()
    {
        if (rotatingTransform == null)
        {
            rotatingTransform = transform;
        }

        if (rotationSpeedDegreesPerSecond < 0f)
        {
            rotationSpeedDegreesPerSecond = 0f;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && rotationScope == RotationScope.Global && startOpen)
        {
            _globalIsOpen.Value = true;
        }

        // Snap local visual immediately to the current desired state.
        SnapVisualToDesiredState();
    }

    private void Update()
    {
        if (rotatingTransform == null || !IsSpawned)
        {
            return;
        }

        // -------------------------------------------------------------
        // GLOBAL ROTATION
        // -------------------------------------------------------------
        // Rotate on every instance, INCLUDING dedicated server.
        // This is required so server-side colliders move correctly for
        // authoritative movement / collision.
        if (rotationScope == RotationScope.Global)
        {
            bool shouldBeOpen = _globalIsOpen.Value;
            Quaternion targetRotation = shouldBeOpen ? _openLocalRotation : _closedLocalRotation;

            rotatingTransform.localRotation = Quaternion.RotateTowards(
                rotatingTransform.localRotation,
                targetRotation,
                rotationSpeedDegreesPerSecond * Time.deltaTime);

            return;
        }

        // -------------------------------------------------------------
        // PER-PLAYER ROTATION
        // -------------------------------------------------------------
        // Only clients/host should animate per-player local visual state.
        // Dedicated server should NOT try to choose one client's per-player
        // visual rotation as the authoritative collider state.
        if (!IsClient && !IsHost)
        {
            return;
        }

        bool shouldBeOpenForLocalClient = GetDesiredOpenStateForLocalClient();
        Quaternion perPlayerTargetRotation = shouldBeOpenForLocalClient ? _openLocalRotation : _closedLocalRotation;

        rotatingTransform.localRotation = Quaternion.RotateTowards(
            rotatingTransform.localRotation,
            perPlayerTargetRotation,
            rotationSpeedDegreesPerSecond * Time.deltaTime);
    }

    /// <summary>
    /// Returns what would happen if this client issued the given command.
    ///
    /// This is used by the action to determine:
    /// - whether the interaction would actually change state
    /// - whether the result would be open or closed
    /// - whether key requirements / item consumption should apply
    ///
    /// IMPORTANT:
    /// This is intended to be called on the server during authoritative interaction execution.
    /// </summary>
    public void PeekCommandResult(
        ulong clientId,
        RotationCommand command,
        out bool willChange,
        out bool resultingOpenState)
    {
        bool currentOpen = GetOpenStateForClientId(clientId);

        switch (command)
        {
            case RotationCommand.Toggle:
                resultingOpenState = !currentOpen;
                break;

            case RotationCommand.OpenOnly:
                resultingOpenState = true;
                break;

            case RotationCommand.CloseOnly:
                resultingOpenState = false;
                break;

            default:
                resultingOpenState = currentOpen;
                break;
        }

        willChange = resultingOpenState != currentOpen;
    }

    /// <summary>
    /// Applies a rotation command on the authoritative server.
    ///
    /// Returns true if the command changed state.
    /// </summary>
    public bool TryApplyCommand(
        ulong clientId,
        RotationCommand command,
        out bool changed,
        out bool resultingOpenState)
    {
        changed = false;
        resultingOpenState = false;

        if (!IsServer)
        {
            Debug.LogWarning("[InteractableRotationState] TryApplyCommand should only be called on the server.", this);
            return false;
        }

        PeekCommandResult(clientId, command, out bool willChange, out resultingOpenState);

        if (!willChange)
        {
            return false;
        }

        if (rotationScope == RotationScope.Global)
        {
            _globalIsOpen.Value = resultingOpenState;
        }
        else
        {
            SetPerPlayerOpenState(clientId, resultingOpenState);
        }

        changed = true;

        if (verboseLogging)
        {
            string scopeText = rotationScope == RotationScope.Global ? "Global" : $"PerPlayer(client {clientId})";
            Debug.Log($"[InteractableRotationState] Applied {command}. Scope={scopeText}, Open={resultingOpenState}", this);
        }

        return true;
    }

    /// <summary>
    /// Returns whether this object is currently open for the specified client ID.
    ///
    /// For global objects, all clients share the same answer.
    /// For per-player objects, membership in the open list decides the answer.
    /// </summary>
    public bool GetOpenStateForClientId(ulong clientId)
    {
        if (rotationScope == RotationScope.Global)
        {
            return _globalIsOpen.Value;
        }

        for (int i = 0; i < _openForClientIds.Count; i++)
        {
            if (_openForClientIds[i] == clientId)
            {
                return true;
            }
        }

        return false;
    }

    private bool GetDesiredOpenStateForLocalClient()
    {
        if (rotationScope == RotationScope.Global)
        {
            return _globalIsOpen.Value;
        }

        if (NetworkManager == null)
        {
            return false;
        }

        return GetOpenStateForClientId(NetworkManager.LocalClientId);
    }

    private void SetPerPlayerOpenState(ulong clientId, bool shouldBeOpen)
    {
        int existingIndex = -1;

        for (int i = 0; i < _openForClientIds.Count; i++)
        {
            if (_openForClientIds[i] == clientId)
            {
                existingIndex = i;
                break;
            }
        }

        if (shouldBeOpen)
        {
            if (existingIndex < 0)
            {
                _openForClientIds.Add(clientId);
            }
        }
        else
        {
            if (existingIndex >= 0)
            {
                _openForClientIds.RemoveAt(existingIndex);
            }
        }
    }

    private void CacheClosedAndOpenRotations()
    {
        if (rotatingTransform == null)
        {
            return;
        }

        _closedLocalRotation = rotatingTransform.localRotation;

        Vector3 axis = GetLocalAxis();
        if (axis.sqrMagnitude < 0.0001f)
        {
            axis = Vector3.up;
        }

        axis.Normalize();

        _openLocalRotation = Quaternion.AngleAxis(openAngleDegrees, axis) * _closedLocalRotation;
    }

    private Vector3 GetLocalAxis()
    {
        switch (rotationAxis)
        {
            case RotationAxis.LocalX:
                return Vector3.right;

            case RotationAxis.LocalY:
                return Vector3.up;

            case RotationAxis.LocalZ:
                return Vector3.forward;

            case RotationAxis.CustomLocal:
                return customLocalAxis;

            default:
                return Vector3.up;
        }
    }

    private void SnapVisualToDesiredState()
    {
        if (rotatingTransform == null)
        {
            return;
        }

        bool shouldBeOpen = GetDesiredOpenStateForLocalClient();
        rotatingTransform.localRotation = shouldBeOpen ? _openLocalRotation : _closedLocalRotation;
    }
}