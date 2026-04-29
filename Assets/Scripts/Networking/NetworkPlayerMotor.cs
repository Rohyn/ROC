using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using ROC.Statuses;

/// <summary>
/// Stage 1 server-authoritative player motor.
///
/// DESIGN:
/// - The owning client gathers raw input locally.
/// - The owning client submits raw movement input + intended yaw to the server.
/// - The server validates input and simulates movement with CharacterController.
/// - The server owns authoritative position and gameplay yaw.
/// - NetworkTransform should replicate authoritative position.
/// - Gameplay yaw is replicated through a server-written NetworkVariable.
///
/// DIRECT MOUSE-LOOK NOTE:
/// - PlayerLookController may still rotate the owning client's root locally for responsive camera feel.
/// - That local owner rotation is treated as presentation/input intent.
/// - The server receives the requested yaw and uses it for authoritative movement/facing.
/// - Non-owner clients apply the replicated gameplay yaw for remote visual facing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkTransform))]
public class NetworkPlayerMotor : NetworkBehaviour
{
    [Header("Movement")]
    [Tooltip("Base ground movement speed in units per second.")]
    [SerializeField] private float moveSpeed = 4.5f;

    [Tooltip("If true, diagonal input is clamped to the same speed as cardinal movement.")]
    [SerializeField] private bool clampDiagonalMovement = true;

    [Header("Jump / Gravity")]
    [Tooltip("How high the player jumps in Unity units. Set to 0 to disable jumping.")]
    [SerializeField] private float jumpHeight = 1.2f;

    [Tooltip("Gravity acceleration. Use a negative value.")]
    [SerializeField] private float gravity = -20f;

    [Tooltip("Small downward force used while grounded to keep CharacterController snapped to ground.")]
    [SerializeField] private float groundedStickForce = -2f;

    [Header("Facing")]
    [Tooltip("If true, the server applies gameplay yaw to the player root.")]
    [SerializeField] private bool serverAppliesGameplayYawToRoot = true;

    [Tooltip("If true, non-owner clients apply replicated gameplay yaw to the player root for remote visual facing.")]
    [SerializeField] private bool nonOwnersApplyReplicatedYawToRoot = true;

    [Tooltip("Minimum yaw delta before the server updates the replicated yaw variable.")]
    [SerializeField] private float yawReplicationThresholdDegrees = 0.1f;

    [Tooltip("Optional yaw rate limit. Set to 0 or less to disable. Direct mouse-look usually wants this disabled for now.")]
    [SerializeField] private float maxYawDegreesPerSecond = 0f;

    [Header("Status Integration")]
    [Tooltip("Optional StatusManager on this player. If empty, this script will look on the same GameObject.")]
    [SerializeField] private StatusManager statusManager;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private CharacterController _characterController;

    // Server-authoritative gameplay yaw.
    private readonly NetworkVariable<float> _gameplayYaw = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Local owner-collected input.
    private Vector2 _localMoveInput;
    private bool _localJumpQueued;
    private uint _localInputSequence;

    // Server-owned input state.
    private Vector2 _serverMoveInput;
    private bool _serverJumpQueued;
    private float _serverYawDegrees;
    private float _lastYawInputReceiveTime;
    private uint _lastProcessedInputSequence;

    // Server-owned movement state.
    private float _verticalVelocity;

    public float GameplayYawDegrees => _gameplayYaw.Value;

    public Vector3 GameplayForward
    {
        get
        {
            float yaw = IsServer ? _serverYawDegrees : _gameplayYaw.Value;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }
    }

    public bool IsGrounded => _characterController != null && _characterController.isGrounded;

    public float CurrentMoveSpeed
    {
        get
        {
            float modifier = 1f;

            if (statusManager != null)
            {
                modifier = statusManager.GetMultiplicativeModifier(StatusModifierType.MoveSpeed);
            }

            return moveSpeed * Mathf.Max(0f, modifier);
        }
    }

    public uint LastProcessedInputSequence => _lastProcessedInputSequence;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();

        if (_characterController == null)
        {
            Debug.LogError("[NetworkPlayerMotor] Missing CharacterController.", this);
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
        if (IsServer)
        {
            _serverYawDegrees = NormalizeYaw(transform.eulerAngles.y);
            _gameplayYaw.Value = _serverYawDegrees;
            _lastYawInputReceiveTime = Time.time;
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        CollectOwnerInput();
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            PushOwnerInputToServer();
        }

        if (IsServer)
        {
            SimulateServerMovement(Time.fixedDeltaTime);
        }
    }

    private void LateUpdate()
    {
        // Non-owner clients do not run local look.
        // Apply replicated gameplay yaw so remote players face correctly
        // even if NetworkTransform rotation sync is disabled.
        if (!IsSpawned || IsServer || IsOwner)
        {
            return;
        }

        if (nonOwnersApplyReplicatedYawToRoot)
        {
            ApplyYawToRoot(_gameplayYaw.Value);
        }
    }

    private void CollectOwnerInput()
    {
        _localMoveInput = ReadMoveInput();

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            _localJumpQueued = true;
        }
    }

    private void PushOwnerInputToServer()
    {
        Vector2 moveInput = clampDiagonalMovement
            ? Vector2.ClampMagnitude(_localMoveInput, 1f)
            : _localMoveInput;

        float requestedYaw = NormalizeYaw(transform.eulerAngles.y);
        bool jumpPressed = _localJumpQueued;

        _localInputSequence++;

        if (IsServer)
        {
            ApplySubmittedInputOnServer(moveInput, requestedYaw, jumpPressed, _localInputSequence);
        }
        else
        {
            SubmitMovementInputRpc(moveInput, requestedYaw, jumpPressed, _localInputSequence);
        }

        _localJumpQueued = false;
    }

    [Rpc(SendTo.Server)]
    private void SubmitMovementInputRpc(
        Vector2 moveInput,
        float requestedYawDegrees,
        bool jumpPressed,
        uint inputSequence)
    {
        ApplySubmittedInputOnServer(moveInput, requestedYawDegrees, jumpPressed, inputSequence);
    }

    private void ApplySubmittedInputOnServer(
        Vector2 moveInput,
        float requestedYawDegrees,
        bool jumpPressed,
        uint inputSequence)
    {
        if (!IsServer)
        {
            return;
        }

        if (!IsFinite(moveInput) || !IsFinite(requestedYawDegrees))
        {
            if (verboseLogging)
            {
                Debug.LogWarning("[NetworkPlayerMotor] Rejected non-finite movement input.", this);
            }

            return;
        }

        _serverMoveInput = clampDiagonalMovement
            ? Vector2.ClampMagnitude(moveInput, 1f)
            : ClampAxes(moveInput, -1f, 1f);

        float normalizedRequestedYaw = NormalizeYaw(requestedYawDegrees);
        _serverYawDegrees = ValidateAndApplyYaw(normalizedRequestedYaw);

        if (jumpPressed)
        {
            _serverJumpQueued = true;
        }

        _lastProcessedInputSequence = inputSequence;

        ReplicateGameplayYawIfNeeded();

        if (verboseLogging)
        {
            Debug.Log(
                $"[NetworkPlayerMotor] Input seq={inputSequence} move={_serverMoveInput} yaw={_serverYawDegrees:F1} jump={jumpPressed}",
                this);
        }
    }

    private void SimulateServerMovement(float deltaTime)
    {
        bool noMovement = statusManager != null && statusManager.HasFlag(StatusFlags.NoMovement);

        Vector3 moveDirection = noMovement
            ? Vector3.zero
            : CalculateMoveWorldFromInputAndYaw(_serverMoveInput, _serverYawDegrees);

        HandleGroundingAndGravity(deltaTime);

        if (!noMovement)
        {
            HandleJumpServer();
        }

        Vector3 horizontalVelocity = moveDirection * CurrentMoveSpeed;
        Vector3 verticalVelocity = Vector3.up * _verticalVelocity;
        Vector3 finalVelocity = horizontalVelocity + verticalVelocity;

        _characterController.Move(finalVelocity * deltaTime);

        if (serverAppliesGameplayYawToRoot)
        {
            ApplyYawToRoot(_serverYawDegrees);
        }

        ReplicateGameplayYawIfNeeded();

        _serverJumpQueued = false;
    }

    private Vector3 CalculateMoveWorldFromInputAndYaw(Vector2 moveInput, float yawDegrees)
    {
        Vector2 input = clampDiagonalMovement
            ? Vector2.ClampMagnitude(moveInput, 1f)
            : ClampAxes(moveInput, -1f, 1f);

        Quaternion yawRotation = Quaternion.Euler(0f, yawDegrees, 0f);

        Vector3 forward = yawRotation * Vector3.forward;
        Vector3 right = yawRotation * Vector3.right;

        Vector3 moveDirection = (forward * input.y) + (right * input.x);

        if (clampDiagonalMovement)
        {
            moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);
        }

        moveDirection.y = 0f;
        return moveDirection;
    }

    private void HandleGroundingAndGravity(float deltaTime)
    {
        if (_characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = groundedStickForce;
        }

        _verticalVelocity += gravity * deltaTime;
    }

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

    private float ValidateAndApplyYaw(float requestedYawDegrees)
    {
        if (maxYawDegreesPerSecond <= 0f)
        {
            _lastYawInputReceiveTime = Time.time;
            return requestedYawDegrees;
        }

        float now = Time.time;
        float elapsed = Mathf.Max(0.001f, now - _lastYawInputReceiveTime);
        _lastYawInputReceiveTime = now;

        float maxDelta = maxYawDegreesPerSecond * elapsed;
        float currentYaw = _serverYawDegrees;
        float delta = Mathf.DeltaAngle(currentYaw, requestedYawDegrees);
        float clampedDelta = Mathf.Clamp(delta, -maxDelta, maxDelta);

        return NormalizeYaw(currentYaw + clampedDelta);
    }

    private void ReplicateGameplayYawIfNeeded()
    {
        if (!IsServer)
        {
            return;
        }

        if (Mathf.Abs(Mathf.DeltaAngle(_gameplayYaw.Value, _serverYawDegrees)) < yawReplicationThresholdDegrees)
        {
            return;
        }

        _gameplayYaw.Value = _serverYawDegrees;
    }

    private void ApplyYawToRoot(float yawDegrees)
    {
        transform.rotation = Quaternion.Euler(0f, NormalizeYaw(yawDegrees), 0f);
    }

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

        return clampDiagonalMovement
            ? Vector2.ClampMagnitude(input, 1f)
            : ClampAxes(input, -1f, 1f);
    }

    private static Vector2 ClampAxes(Vector2 value, float min, float max)
    {
        value.x = Mathf.Clamp(value.x, min, max);
        value.y = Mathf.Clamp(value.y, min, max);
        return value;
    }

    private static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.x) && IsFinite(value.y);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float NormalizeYaw(float yawDegrees)
    {
        if (!IsFinite(yawDegrees))
        {
            return 0f;
        }

        yawDegrees %= 360f;

        if (yawDegrees < 0f)
        {
            yawDegrees += 360f;
        }

        return yawDegrees;
    }
}