using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-pass third-person direct mouselook controller with settings/keybind support.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLookController : NetworkBehaviour
{
    public enum CursorModeState
    {
        GameplayLocked = 0,
        TemporaryFreeCursor = 1,
        MenuCursor = 2,
        ConversationCursor = 3
    }

    [Header("Required References")]
    [Tooltip("The pivot transform that controls camera pitch. Usually this is your CameraPivot child object.")]
    [SerializeField] private Transform cameraPivot;

    [Header("Look Tuning")]
    [Tooltip("Base horizontal mouse sensitivity before account sensitivity multiplier.")]
    [SerializeField] private float baseYawSensitivity = 0.12f;

    [Tooltip("Base vertical mouse sensitivity before account sensitivity multiplier.")]
    [SerializeField] private float basePitchSensitivity = 0.10f;

    [Tooltip("Minimum vertical pitch angle in degrees.")]
    [SerializeField] private float minPitch = -35f;

    [Tooltip("Maximum vertical pitch angle in degrees.")]
    [SerializeField] private float maxPitch = 60f;

    [Header("Fallback Keys")]
    [Tooltip("Used only if settings cannot resolve a binding.")]
    [SerializeField] private Key fallbackToggleFreeCursorKey = Key.Period;

    [Tooltip("Used only if settings cannot resolve a binding.")]
    [SerializeField] private Key fallbackToggleMenuKey = Key.Tab;

    [Header("Startup")]
    [Tooltip("If true, the player starts in locked gameplay look mode when they spawn.")]
    [SerializeField] private bool startLockedInGameplay = true;

    public event Action<CursorModeState> CursorModeChanged;

    public CursorModeState CurrentCursorMode { get; private set; } = CursorModeState.GameplayLocked;

    private float _pitchDegrees;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        GameSettingsService.GetOrCreate();

        if (cameraPivot == null)
        {
            Debug.LogError("[PlayerLookController] No cameraPivot assigned.", this);
            enabled = false;
            return;
        }

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

        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return;
        }

        HandleModeToggleInput();
        HandleMovementRelock();

        if (CurrentCursorMode == CursorModeState.GameplayLocked)
        {
            ApplyMouseLook(mouse);
        }
    }

    private void HandleModeToggleInput()
    {
        if (CurrentCursorMode == CursorModeState.ConversationCursor)
        {
            return;
        }

        if (RocInput.WasPressedThisFrame(KeybindActionId.ToggleFreeCursor, fallbackToggleFreeCursorKey))
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

        if (RocInput.WasPressedThisFrame(KeybindActionId.ToggleMenu, fallbackToggleMenuKey))
        {
            if (CurrentCursorMode == CursorModeState.MenuCursor)
            {
                SetCursorMode(CursorModeState.GameplayLocked);
            }
            else
            {
                SetCursorMode(CursorModeState.MenuCursor);
            }
        }
    }

    private void HandleMovementRelock()
    {
        if (CurrentCursorMode != CursorModeState.TemporaryFreeCursor)
        {
            return;
        }

        bool movementPressed =
            RocInput.IsPressed(KeybindActionId.MoveForward, Key.W) ||
            RocInput.IsPressed(KeybindActionId.MoveBackward, Key.S) ||
            RocInput.IsPressed(KeybindActionId.MoveLeft, Key.A) ||
            RocInput.IsPressed(KeybindActionId.MoveRight, Key.D);

        if (movementPressed)
        {
            SetCursorMode(CursorModeState.GameplayLocked);
        }
    }

    private void ApplyMouseLook(Mouse mouse)
    {
        AccountSettingsData accountSettings = GameSettingsService.Instance.AccountSettings;
        float sensitivityMultiplier = accountSettings != null ? accountSettings.mouseSensitivity : 1f;
        bool invertY = accountSettings != null && accountSettings.invertY;

        Vector2 mouseDelta = mouse.delta.ReadValue();

        float yawDelta = mouseDelta.x * baseYawSensitivity * sensitivityMultiplier;
        transform.Rotate(0f, yawDelta, 0f, Space.Self);

        float pitchInput = mouseDelta.y * basePitchSensitivity * sensitivityMultiplier;

        if (!invertY)
        {
            pitchInput = -pitchInput;
        }

        _pitchDegrees = Mathf.Clamp(_pitchDegrees + pitchInput, minPitch, maxPitch);
        cameraPivot.localRotation = Quaternion.Euler(_pitchDegrees, 0f, 0f);
    }

    public void SetCursorMode(CursorModeState newMode)
    {
        CurrentCursorMode = newMode;

        switch (CurrentCursorMode)
        {
            case CursorModeState.GameplayLocked:
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                break;

            case CursorModeState.TemporaryFreeCursor:
            case CursorModeState.MenuCursor:
            case CursorModeState.ConversationCursor:
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
        }

        CursorModeChanged?.Invoke(CurrentCursorMode);
        Debug.Log($"[PlayerLookController] Cursor mode changed to {CurrentCursorMode}.", this);
    }

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

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
