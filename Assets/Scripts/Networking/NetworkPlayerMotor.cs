using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using ROC.Statuses;

/// <summary>
/// Very first-pass player motor for testing character feel.
///
/// PURPOSE OF THIS SCRIPT:
/// - Make the owning client's player controllable
/// - Use CharacterController for movement
/// - Move relative to the current camera direction
/// - Apply gravity
/// - Rotate the player toward their movement direction
///
/// IMPORTANT:
/// This is a LOCAL FEEL-TEST controller, not the final MMO movement architecture.
/// It is intentionally simple so you can start tuning movement immediately.
///
/// Later, we should replace or extend this with a more server-authoritative design.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class NetworkPlayerMotor : NetworkBehaviour
{
    [Header("Movement")]
    [Tooltip("How fast the player moves on the ground in units per second.")]
    [SerializeField] private float moveSpeed = 4.5f;

    [Tooltip("How quickly the player rotates to face their movement direction.")]
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Jump / Gravity")]
    [Tooltip("How high the player jumps in Unity units. Set to 0 if you want to disable jumping for now.")]
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

    // Cached CharacterController reference.
    private CharacterController _characterController;

    // Vertical velocity used for gravity and jumping.
    private float _verticalVelocity;

    // Cached camera transform used to convert input into camera-relative movement.
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
        // This controller should only process input for the owning client.
        // Remote players should NOT try to read local keyboard input.
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

        // ---------------------------------------------------------------------
        // Read movement input.
        // ---------------------------------------------------------------------
        Vector2 moveInput = ReadMoveInput();

        // ---------------------------------------------------------------------
        // Query status restrictions and modifiers.
        // ---------------------------------------------------------------------
        bool noMovement = statusManager != null && statusManager.HasFlag(StatusFlags.NoMovement);
        bool noRotation = statusManager != null && statusManager.HasFlag(StatusFlags.NoRotation);

        float movementMultiplier = 1f;
        if (statusManager != null)
        {
            movementMultiplier = statusManager.GetMultiplicativeModifier(StatusModifierType.MoveSpeed);
        }

        // If movement is blocked by statuses like Resting or Frozen,
        // zero out the movement input before we convert it into world-space movement.
        if (noMovement)
        {
            moveInput = Vector2.zero;
        }

        Vector3 moveDirection = CalculateCameraRelativeMove(moveInput);

        HandleGroundingAndGravity();

        // Only allow jumping if movement is not locked.
        if (!noMovement)
        {
            HandleJumpInput();
        }

        Vector3 finalVelocity =
            (moveDirection * moveSpeed * movementMultiplier) +
            (Vector3.up * _verticalVelocity);

        _characterController.Move(finalVelocity * Time.deltaTime);

        if (!noRotation)
        {
            RotateTowardMovement(moveDirection);
        }
    }

    /// <summary>
    /// Reads keyboard movement input using the Input System package.
    ///
    /// Returns:
    /// - X = left/right
    /// - Y = forward/back
    ///
    /// This is intentionally simple for now:
    /// - W / S control forward and backward
    /// - A / D control left and right
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

        // Normalize diagonal input so moving diagonally is not faster.
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

        // Flatten the camera forward vector onto the ground plane.
        // We do not want movement to tilt upward/downward just because the camera is pitched.
        Vector3 forward = referenceTransform.forward;
        forward.y = 0f;
        forward.Normalize();

        // Flatten the camera right vector onto the ground plane as well.
        Vector3 right = referenceTransform.right;
        right.y = 0f;
        right.Normalize();

        // Combine forward/back and left/right input into one world-space direction.
        Vector3 moveDirection =
            (forward * moveInput.y) +
            (right * moveInput.x);

        // Clamp to avoid diagonal speed increases after vector combination.
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        return moveDirection;
    }

    /// <summary>
    /// Handles grounded behavior and gravity accumulation.
    ///
    /// CharacterController.isGrounded reflects the result of the last movement step,
    /// so we use it each frame to decide how vertical velocity should behave.
    /// </summary>
    private void HandleGroundingAndGravity()
    {
        if (_characterController.isGrounded)
        {
            // When grounded, keep a slight downward velocity rather than leaving Y at zero.
            // This helps the controller stay grounded more reliably on uneven surfaces.
            if (_verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickForce;
            }
        }

        // Gravity is always applied every frame.
        _verticalVelocity += gravity * Time.deltaTime;
    }

    /// <summary>
    /// Handles jump input.
    ///
    /// Spacebar will launch the player upward only if grounded.
    /// If you want no jumping yet, set jumpHeight to 0.
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
            // Basic jump formula:
            // v = sqrt( jumpHeight * -2 * gravity )
            //
            // Since gravity is negative, this produces a positive upward launch velocity.
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    /// <summary>
    /// Smoothly rotates the player to face the movement direction.
    ///
    /// If there is no movement input, the player keeps their current facing.
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
    ///
    /// Preference order:
    /// 1. Explicit Inspector-assigned transform
    /// 2. Camera.main
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