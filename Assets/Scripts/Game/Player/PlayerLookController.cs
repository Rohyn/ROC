using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// First-pass third-person direct mouselook controller.
///
/// DESIGN GOAL:
/// - Default gameplay uses direct mouselook (ESO-like feel).
/// - The player root rotates on mouse X (yaw).
/// - The camera pivot rotates on mouse Y (pitch).
/// - The cursor is normally locked during gameplay.
/// - Pressing '.' toggles a temporary free-cursor mode.
/// - Pressing Tab toggles a menu cursor mode.
/// - Movement input relocks the cursor if the player was only in temporary free-cursor mode.
///
/// IMPORTANT:
/// This script only runs for the owning client.
/// Remote players should not process local mouse input.
/// </summary>
public class PlayerLookController : NetworkBehaviour
{
    /// <summary>
    /// Distinguishes between the different cursor / look states.
    /// </summary>
    public enum CursorModeState
    {
        /// <summary>
        /// Normal gameplay:
        /// - cursor locked
        /// - mouse moves camera
        /// </summary>
        GameplayLocked = 0,

        /// <summary>
        /// Temporary free cursor:
        /// - cursor unlocked
        /// - mouse does NOT move camera
        /// - intended for quick UI clicks
        /// - movement input relocks back to gameplay
        /// </summary>
        TemporaryFreeCursor = 1,

        /// <summary>
        /// Menu mode:
        /// - cursor unlocked
        /// - mouse does NOT move camera
        /// - intended for inventory / map / journal / menu tabs later
        /// - does NOT auto-close on movement in this first version
        /// </summary>
        MenuCursor = 2,

        /// <summary>
        /// Conversation mode:
        /// - cursor unlocked
        /// - mouse does NOT move camera
        /// - menu should remain closed
        /// - used while speaking with an NPC
        /// </summary>
        ConversationCursor = 3
    }

    [Header("Required References")]
    [Tooltip("The pivot transform that controls camera pitch. Usually this is your CameraPivot child object.")]
    [SerializeField] private Transform cameraPivot;

    [Header("Look Tuning")]
    [Tooltip("Horizontal mouse sensitivity multiplier.")]
    [SerializeField] private float yawSensitivity = 0.12f;

    [Tooltip("Vertical mouse sensitivity multiplier.")]
    [SerializeField] private float pitchSensitivity = 0.10f;

    [Tooltip("Minimum vertical pitch angle in degrees.")]
    [SerializeField] private float minPitch = -35f;

    [Tooltip("Maximum vertical pitch angle in degrees.")]
    [SerializeField] private float maxPitch = 60f;

    [Tooltip("If true, invert vertical mouse look.")]
    [SerializeField] private bool invertY = false;

    [Header("Cursor / Mode Keys")]
    [Tooltip("Key used to toggle the temporary free-cursor mode.")]
    [SerializeField] private Key toggleFreeCursorKey = Key.Period;

    [Tooltip("Key used to toggle the general menu cursor mode.")]
    [SerializeField] private Key toggleMenuKey = Key.Tab;

    [Header("Startup")]
    [Tooltip("If true, the player starts in locked gameplay look mode when they spawn.")]
    [SerializeField] private bool startLockedInGameplay = true;

    /// <summary>
    /// Optional event so UI or other systems can react later.
    /// For now, this is just a clean extensibility point.
    /// </summary>
    public event Action<CursorModeState> CursorModeChanged;

    /// <summary>
    /// Current cursor / look mode.
    /// </summary>
    public CursorModeState CurrentCursorMode { get; private set; } = CursorModeState.GameplayLocked;

    /// <summary>
    /// Current vertical pitch angle in degrees.
    /// We store this explicitly rather than reading Euler angles back from the transform.
    /// </summary>
    private float _pitchDegrees;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        if (cameraPivot == null)
        {
            Debug.LogError("[PlayerLookController] No cameraPivot assigned.");
            enabled = false;
            return;
        }

        // Initialize pitch from the pivot's current local rotation if desired.
        // For this first version, we start from zero for clarity.
        _pitchDegrees = 0f;

        if (startLockedInGameplay)
        {
            SetCursorMode(CursorModeState.GameplayLocked);
        }
        else
        {
            SetCursorMode(CursorModeState.TemporaryFreeCursor);
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null || mouse == null)
        {
            return;
        }

        HandleModeToggleInput(keyboard);
        HandleMovementRelock(keyboard);

        if (CurrentCursorMode == CursorModeState.GameplayLocked)
        {
            ApplyMouseLook(mouse);
        }
    }

    /// <summary>
    /// Processes the three cursor-mode toggle keys.
    /// </summary>
    private void HandleModeToggleInput(Keyboard keyboard)
    {
        // During conversation, do not allow '.' or Tab to change cursor mode.
        // Conversation open/close should be controlled by the conversation system.
        if (CurrentCursorMode == CursorModeState.ConversationCursor)
        {
            return;
        }

        // '.' toggles the temporary free-cursor mode.
        if (keyboard[toggleFreeCursorKey].wasPressedThisFrame)
        {
            if (CurrentCursorMode == CursorModeState.MenuCursor)
            {
                return;
            }

            if (CurrentCursorMode == CursorModeState.TemporaryFreeCursor)
            {
                SetCursorMode(CursorModeState.GameplayLocked);
            }
            else
            {
                SetCursorMode(CursorModeState.TemporaryFreeCursor);
            }

            return;
        }

        // Tab toggles the menu mode.
        if (keyboard[toggleMenuKey].wasPressedThisFrame)
        {
            if (CurrentCursorMode == CursorModeState.MenuCursor)
            {
                SetCursorMode(CursorModeState.GameplayLocked);
            }
            else
            {
                SetCursorMode(CursorModeState.MenuCursor);
            }

            return;
        }
    }

    /// <summary>
    /// If the player is only in temporary free-cursor mode,
    /// movement input relocks them back into gameplay.
    ///
    /// This does NOT affect the full menu mode.
    /// </summary>
    private void HandleMovementRelock(Keyboard keyboard)
    {
        if (CurrentCursorMode != CursorModeState.TemporaryFreeCursor)
        {
            return;
        }

        bool movementPressed =
            keyboard.wKey.isPressed ||
            keyboard.aKey.isPressed ||
            keyboard.sKey.isPressed ||
            keyboard.dKey.isPressed;

        if (movementPressed)
        {
            SetCursorMode(CursorModeState.GameplayLocked);
        }
    }

    /// <summary>
    /// Applies mouse look to:
    /// - player root yaw
    /// - camera pivot pitch
    /// </summary>
    private void ApplyMouseLook(Mouse mouse)
    {
        Vector2 mouseDelta = mouse.delta.ReadValue();

        // Horizontal mouse movement rotates the player root around Y.
        float yawDelta = mouseDelta.x * yawSensitivity;
        transform.Rotate(0f, yawDelta, 0f, Space.Self);

        // Vertical mouse movement rotates only the camera pivot.
        float pitchInput = mouseDelta.y * pitchSensitivity;

        if (!invertY)
        {
            // Standard look behavior:
            // moving mouse up should look upward, which means decreasing downward pitch.
            pitchInput = -pitchInput;
        }

        _pitchDegrees = Mathf.Clamp(_pitchDegrees + pitchInput, minPitch, maxPitch);

        cameraPivot.localRotation = Quaternion.Euler(_pitchDegrees, 0f, 0f);
    }

    /// <summary>
    /// Centralized cursor mode setter.
    ///
    /// This is where we control:
    /// - Cursor.lockState
    /// - Cursor.visible
    /// - internal mode tracking
    /// </summary>
    public void SetCursorMode(CursorModeState newMode)
    {
        CurrentCursorMode = newMode;

        switch (CurrentCursorMode)
        {
            case CursorModeState.GameplayLocked:
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                break;
            }

            case CursorModeState.TemporaryFreeCursor:
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
            }

            case CursorModeState.MenuCursor:
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
            }

            case CursorModeState.ConversationCursor:
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
            }
        }

        CursorModeChanged?.Invoke(CurrentCursorMode);

        Debug.Log($"[PlayerLookController] Cursor mode changed to {CurrentCursorMode}.");
    }

    /// <summary>
    /// Useful helper if other systems later need to ask whether camera look is active.
    /// </summary>
    public bool IsGameplayLookActive()
    {
        return CurrentCursorMode == CursorModeState.GameplayLocked;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            return;
        }

        // Be polite on despawn / scene unload.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}