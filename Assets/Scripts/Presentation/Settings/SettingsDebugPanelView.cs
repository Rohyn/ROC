using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Very small temporary settings/debug panel.
/// This is not meant to be final UI.
/// It lets you verify that settings are loading/saving and keybinds can reset.
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsDebugPanelView : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TMP_Text bodyText;

    [Header("Buttons")]
    [SerializeField] private Button resetKeybindsButton;
    [SerializeField] private Button resetAccountSettingsButton;

    private void OnEnable()
    {
        if (resetKeybindsButton != null)
        {
            resetKeybindsButton.onClick.AddListener(HandleResetKeybindsClicked);
        }

        if (resetAccountSettingsButton != null)
        {
            resetAccountSettingsButton.onClick.AddListener(HandleResetAccountSettingsClicked);
        }

        GameSettingsService.Instance.SettingsChanged += Refresh;
        GameSettingsService.Instance.KeybindsChanged += Refresh;

        Refresh();
    }

    private void OnDisable()
    {
        if (resetKeybindsButton != null)
        {
            resetKeybindsButton.onClick.RemoveListener(HandleResetKeybindsClicked);
        }

        if (resetAccountSettingsButton != null)
        {
            resetAccountSettingsButton.onClick.RemoveListener(HandleResetAccountSettingsClicked);
        }

        GameSettingsService settingsService = GameSettingsService.GetOrCreate();

        if (settingsService != null)
        {
            settingsService.SettingsChanged -= Refresh;
            settingsService.KeybindsChanged -= Refresh;
        }
    }

    public void Refresh()
    {
        if (bodyText == null)
        {
            return;
        }

        AccountSettingsData account = GameSettingsService.Instance.AccountSettings;

        bodyText.text =
            "Settings Foundation v0\n" +
            $"Master Volume: {account.masterVolume:0.00}\n" +
            $"Mouse Sensitivity: {account.mouseSensitivity:0.00}\n" +
            $"Invert Y: {account.invertY}\n\n" +
            $"Move Forward: {RocInput.GetDisplayName(KeybindActionId.MoveForward, Key.W)}\n" +
            $"Move Backward: {RocInput.GetDisplayName(KeybindActionId.MoveBackward, Key.S)}\n" +
            $"Move Left: {RocInput.GetDisplayName(KeybindActionId.MoveLeft, Key.A)}\n" +
            $"Move Right: {RocInput.GetDisplayName(KeybindActionId.MoveRight, Key.D)}\n" +
            $"Interact: {RocInput.GetDisplayName(KeybindActionId.Interact, Key.E)}\n" +
            $"Toggle Menu: {RocInput.GetDisplayName(KeybindActionId.ToggleMenu, Key.Tab)}\n" +
            $"Free Cursor: {RocInput.GetDisplayName(KeybindActionId.ToggleFreeCursor, Key.Period)}";
    }

    private void HandleResetKeybindsClicked()
    {
        GameSettingsService.Instance.ResetKeybindsToDefault();
        Refresh();
    }

    private void HandleResetAccountSettingsClicked()
    {
        GameSettingsService.Instance.ResetAccountSettingsToDefault();
        Refresh();
    }
}
