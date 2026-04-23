using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using ROC.Statuses;

/// <summary>
/// First-pass local player motor for testing third-person movement feel.
///
/// DESIGN GOALS:
/// - Use CharacterController for simple grounded movement
/// - Move relative to the current camera facing
/// - Apply gravity and optional jump
/// - Respect status flags such as NoMovement and NoRotation
/// - Optionally rotate toward movement direction
///
/// IMPORTANT:
/// This is still a local feel-test controller, not final server-authoritative MMO movement.
/// It is meant to pair with PlayerLookController:
/// - direct mouselook should usually control facing
/// - rotateTowardMovement should usually be FALSE for that setup
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class NetworkPlayerMotor : NetworkBehaviour
{
    [Header("Movement")]
    [Tooltip("How fast the player moves on the ground in units per second.")]
    [SerializeField] private float moveSpeed = 4.5f;

    [Tooltip("How quickly the player rotates to face movement direction when rotateTowardMovement is enabled.")]
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Facing")]
    [Tooltip("If true, the player rotates to face movement direction. For direct mouselook, leave this false.")]
    [SerializeField] private bool rotateTowardMovement = false;

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

    private CharacterController _characterController;
    private float _verticalVelocity;
    private Transform _cameraTransform;

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
        // Only the owning client should process local movement input.
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        CacheCameraReference();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (_cameraTransform == null)
        {
            CacheCameraReference();
        }

        Vector2 moveInput = ReadMoveInput();

        bool noMovement = statusManager != null && statusManager.HasFlag(StatusFlags.NoMovement);
        bool noRotation = statusManager != null && statusManager.HasFlag(StatusFlags.NoRotation);

        float movementMultiplier = 1f;
        if (statusManager != null)
        {
            movementMultiplier = statusManager.GetMultiplicativeModifier(StatusModifierType.MoveSpeed);
        }

        // If movement is blocked, ignore movement input entirely.
        if (noMovement)
        {
            moveInput = Vector2.zero;
        }

        Vector3 moveDirection = CalculateCameraRelativeMove(moveInput);

        HandleGroundingAndGravity();

        // Only allow jumping when movement is not blocked.
        if (!noMovement)
        {
            HandleJumpInput();
        }

        Vector3 finalVelocity =
            (moveDirection * moveSpeed * movementMultiplier) +
            (Vector3.up * _verticalVelocity);

        _characterController.Move(finalVelocity * Time.deltaTime);

        // In direct mouselook mode, leave this OFF so mouse/controller look owns facing.
        if (!noRotation && rotateTowardMovement)
        {
            RotateTowardMovement(moveDirection);
        }
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

        // Prevent diagonal movement from being faster than straight movement.
        input = Vector2.ClampMagnitude(input, 1f);

        return input;
    }

    /// <summary>
    /// Converts 2D input into a camera-relative world movement vector.
    ///
    /// If no camera exists yet, the script falls back to the player's own forward/right axes.
    /// </summary>
    private Vector3 CalculateCameraRelativeMove(Vector2 moveInput)
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
    /// Handles grounded behavior and gravity accumulation.
    /// CharacterController.isGrounded reflects the result of the last move step.
    /// </summary>
    private void HandleGroundingAndGravity()
    {
        if (_characterController.isGrounded)
        {
            if (_verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickForce;
            }
        }

        _verticalVelocity += gravity * Time.deltaTime;
    }

    /// <summary>
    /// Handles jump input using Space.
    /// </summary>
    private void HandleJumpInput()
    {
        if (jumpHeight <= 0f)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (_characterController.isGrounded && keyboard.spaceKey.wasPressedThisFrame)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    /// <summary>
    /// Smoothly rotates the player to face movement direction.
    /// Only used when rotateTowardMovement is enabled.
    /// </summary>
    private void RotateTowardMovement(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Finds the camera transform used for camera-relative movement.
    /// </summary>
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