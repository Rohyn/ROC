using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Netcode.Components;
using ROC.Statuses;

/// <summary>
/// First-pass server-authoritative player motor.
///
/// DESIGN GOALS:
/// - The owning client gathers input locally.
/// - The owning client sends desired movement input to the server.
/// - The server simulates movement using CharacterController.
/// - NetworkTransform replicates the authoritative position to all clients.
///
/// IMPORTANT:
/// - This version intentionally focuses on authoritative POSITION first.
/// - Rotation is left local for now so your direct-mouselook camera stays responsive.
/// - That means NetworkTransform rotation sync should be disabled for now.
/// - Later, we can make gameplay-facing authoritative too.
///
/// This script also reads server-side status flags/modifiers,
/// which means once interactions are executed on the server,
/// statuses like Resting can correctly block authoritative movement.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkTransform))]
public class NetworkPlayerMotor : NetworkBehaviour
{
    [Header("Movement")]
    [Tooltip("How fast the player moves on the ground in units per second.")]
    [SerializeField] private float moveSpeed = 4.5f;

    [Header("Jump / Gravity")]
    [Tooltip("How high the player jumps in Unity units. Set to 0 to disable jumping for now.")]
    [SerializeField] private float jumpHeight = 1.2f;

    [Tooltip("Gravity acceleration applied to the player. Negative value because down is negative Y.")]
    [SerializeField] private float gravity = -20f;

    [Tooltip("Small downward force used while grounded to help the CharacterController stay snapped to the ground.")]
    [SerializeField] private float groundedStickForce = -2f;

    [Header("Camera Reference")]
    [Tooltip("Optional explicit camera transform. If left empty, the script will try to use Camera.main.")]
    [SerializeField] private Transform cameraTransformOverride;

    [Header("Status Integration")]
    [Tooltip("Optional reference to the StatusManager on this player. If left empty, the script will try to find one on the same GameObject.")]
    [SerializeField] private StatusManager statusManager;

    [Header("Debug")]
    [Tooltip("If true, input submission and server movement events will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private CharacterController _characterController;
    private Transform _cameraTransform;

    // -----------------------------
    // Local owner-collected input
    // -----------------------------
    private Vector2 _localMoveInput;
    private bool _localJumpQueued;
    private Vector3 _localDesiredMoveWorld;

    // -----------------------------
    // Server-authoritative input state
    // -----------------------------
    private Vector3 _serverDesiredMoveWorld;
    private bool _serverJumpQueued;
    private float _verticalVelocity;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();

        if (_characterController == null)
        {
            Debug.LogError("[NetworkPlayerMotor] Missing CharacterController component.");
            enabled = false;
            return;
        }

        if (statusManager == null)
        {
            statusManager = GetComponent<StatusManager>();
        }
    }

    public override void OnNetworkSpawn()
    {
        // We still need this component active on the server for authoritative simulation.
        // Only pure non-owner clients can safely disable it.
        if (!IsOwner && !IsServer)
        {
            enabled = false;
            return;
        }

        if (IsOwner)
        {
            CacheCameraReference();
        }
    }

    private void Update()
    {
        // Only the owning client should gather local input.
        if (!IsOwner)
        {
            return;
        }

        if (_cameraTransform == null)
        {
            CacheCameraReference();
        }

        CollectLocalInput();
    }

    private void FixedUpdate()
    {
        // Owner sends latest input to authority.
        if (IsOwner)
        {
            PushLocalInputToAuthority();
        }

        // Server simulates the authoritative movement.
        if (IsServer)
        {
            SimulateAuthoritativeMovement(Time.fixedDeltaTime);
        }
    }

    /// <summary>
    /// Collects local owner input and converts movement into a world-space desired move vector.
    /// </summary>
    private void CollectLocalInput()
    {
        _localMoveInput = ReadMoveInput();

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            _localJumpQueued = true;
        }

        _localDesiredMoveWorld = CalculateCameraRelativeMoveWorld(_localMoveInput);
    }

    /// <summary>
    /// Sends the current local input state to the server.
    ///
    /// On host, we can assign directly without sending an RPC to ourselves.
    /// </summary>
    private void PushLocalInputToAuthority()
    {
        Vector3 desiredMoveWorld = _localDesiredMoveWorld;
        bool jumpPressed = _localJumpQueued;

        if (IsServer)
        {
            _serverDesiredMoveWorld = desiredMoveWorld;

            if (jumpPressed)
            {
                _serverJumpQueued = true;
            }
        }
        else
        {
            SubmitMovementInputRpc(desiredMoveWorld, jumpPressed);
        }

        // Jump is a one-shot input. Once sent, clear the local queue.
        _localJumpQueued = false;
    }

    /// <summary>
    /// Client -> Server input submission.
    ///
    /// This is the core of the server-authoritative movement path:
    /// the client sends desired movement, but only the server actually moves the player.
    /// </summary>
    [Rpc(SendTo.Server)]
    private void SubmitMovementInputRpc(Vector3 desiredMoveWorld, bool jumpPressed)
    {
        // Flatten and clamp for safety.
        desiredMoveWorld.y = 0f;
        desiredMoveWorld = Vector3.ClampMagnitude(desiredMoveWorld, 1f);

        _serverDesiredMoveWorld = desiredMoveWorld;

        if (jumpPressed)
        {
            _serverJumpQueued = true;
        }

        if (verboseLogging)
        {
            Debug.Log($"[NetworkPlayerMotor] Server received input. Move={desiredMoveWorld}, Jump={jumpPressed}");
        }
    }

    /// <summary>
    /// Runs only on the server and applies authoritative movement.
    /// </summary>
    private void SimulateAuthoritativeMovement(float deltaTime)
    {
        bool noMovement = statusManager != null && statusManager.HasFlag(StatusFlags.NoMovement);

        float movementMultiplier = 1f;
        if (statusManager != null)
        {
            movementMultiplier = statusManager.GetMultiplicativeModifier(StatusModifierType.MoveSpeed);
        }

        Vector3 moveDirection = noMovement
            ? Vector3.zero
            : Vector3.ClampMagnitude(_serverDesiredMoveWorld, 1f);

        HandleGroundingAndGravity(deltaTime);

        if (!noMovement)
        {
            HandleJumpServer();
        }

        Vector3 finalVelocity =
            (moveDirection * moveSpeed * movementMultiplier) +
            (Vector3.up * _verticalVelocity);

        _characterController.Move(finalVelocity * deltaTime);

        // Jump is also one-shot on the server side.
        _serverJumpQueued = false;
    }

    /// <summary>
    /// Reads simple WASD movement input using the Input System package directly.
    /// </summary>
    private Vector2 ReadMoveInput()
    {
        Vector2 input = Vector2.zero;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return input;
        }

        if (keyboard.wKey.isPressed)
        {
            input.y += 1f;
        }

        if (keyboard.sKey.isPressed)
        {
            input.y -= 1f;
        }

        if (keyboard.dKey.isPressed)
        {
            input.x += 1f;
        }

        if (keyboard.aKey.isPressed)
        {
            input.x -= 1f;
        }

        input = Vector2.ClampMagnitude(input, 1f);
        return input;
    }

    /// <summary>
    /// Converts 2D movement input into a camera-relative world-space move direction.
    ///
    /// Because the server does not know the client's camera orientation,
    /// we do this conversion locally on the owning client and send the resulting world-space direction.
    /// </summary>
    private Vector3 CalculateCameraRelativeMoveWorld(Vector2 moveInput)
    {
        Transform referenceTransform = _cameraTransform != null ? _cameraTransform : transform;

        Vector3 forward = referenceTransform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = referenceTransform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 moveDirection =
            (forward * moveInput.y) +
            (right * moveInput.x);

        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);
        return moveDirection;
    }

    /// <summary>
    /// Handles grounded state and gravity accumulation on the server.
    /// </summary>
    private void HandleGroundingAndGravity(float deltaTime)
    {
        if (_characterController.isGrounded)
        {
            if (_verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickForce;
            }
        }

        _verticalVelocity += gravity * deltaTime;
    }

    /// <summary>
    /// Applies jump on the server if queued and grounded.
    /// </summary>
    private void HandleJumpServer()
    {
        if (jumpHeight <= 0f)
        {
            return;
        }

        if (_serverJumpQueued && _characterController.isGrounded)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    private void CacheCameraReference()
    {
        if (cameraTransformOverride != null)
        {
            _cameraTransform = cameraTransformOverride;
            return;
        }

        if (Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }
    }
}