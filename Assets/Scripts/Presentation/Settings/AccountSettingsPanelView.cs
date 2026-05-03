using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main-menu account settings panel.
/// 
/// This panel intentionally edits only account-level settings.
/// Do not wire character-specific settings or HUD layout here.
/// </summary>
[DisallowMultipleComponent]
public sealed class AccountSettingsPanelView : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private TMP_Text masterVolumeValueText;

    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private TMP_Text musicVolumeValueText;

    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TMP_Text sfxVolumeValueText;

    [Header("Camera / Look")]
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private TMP_Text mouseSensitivityValueText;

    [SerializeField] private Toggle invertYToggle;

    [Header("Graphics")]
    [SerializeField] private Toggle fullscreenToggle;

    [Tooltip("Optional. If assigned, values are read as integer FPS. Use entries like Unlimited, 30, 60, 120.")]
    [SerializeField] private TMP_Dropdown targetFrameRateDropdown;

    [Header("Buttons")]
    [SerializeField] private Button resetAccountSettingsButton;
    [SerializeField] private Button resetKeybindsButton;

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;

    [Header("Behavior")]
    [SerializeField] private bool saveImmediately = true;

    private GameSettingsService _settingsService;
    private bool _isRefreshing;

    private void OnEnable()
    {
        _settingsService = GameSettingsService.GetOrCreate();

        HookControls();
        _settingsService.SettingsChanged += Refresh;
        _settingsService.KeybindsChanged += Refresh;

        Refresh();
    }

    private void OnDisable()
    {
        UnhookControls();

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged -= Refresh;
            _settingsService.KeybindsChanged -= Refresh;
        }

        _settingsService = null;
    }

    public void Refresh()
    {
        if (_settingsService == null)
        {
            _settingsService = GameSettingsService.GetOrCreate();
        }

        AccountSettingsData settings = _settingsService.AccountSettings;

        if (settings == null)
        {
            return;
        }

        settings.EnsureValid();

        _isRefreshing = true;

        SetSlider(masterVolumeSlider, settings.masterVolume, 0f, 1f);
        SetSlider(musicVolumeSlider, settings.musicVolume, 0f, 1f);
        SetSlider(sfxVolumeSlider, settings.sfxVolume, 0f, 1f);
        SetSlider(mouseSensitivitySlider, settings.mouseSensitivity, 0.05f, 5f);

        if (invertYToggle != null)
        {
            invertYToggle.isOn = settings.invertY;
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = settings.fullscreen;
        }

        if (targetFrameRateDropdown != null)
        {
            SelectFrameRateDropdownValue(settings.targetFrameRate);
        }

        _isRefreshing = false;

        RefreshValueTexts();
        SetStatus("Account settings loaded.");
    }

    private void HookControls()
    {
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(HandleMasterVolumeChanged);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.AddListener(HandleMusicVolumeChanged);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(HandleSfxVolumeChanged);
        }

        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.onValueChanged.AddListener(HandleMouseSensitivityChanged);
        }

        if (invertYToggle != null)
        {
            invertYToggle.onValueChanged.AddListener(HandleInvertYChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.AddListener(HandleFullscreenChanged);
        }

        if (targetFrameRateDropdown != null)
        {
            targetFrameRateDropdown.onValueChanged.AddListener(HandleTargetFrameRateDropdownChanged);
        }

        if (resetAccountSettingsButton != null)
        {
            resetAccountSettingsButton.onClick.AddListener(HandleResetAccountSettingsClicked);
        }

        if (resetKeybindsButton != null)
        {
            resetKeybindsButton.onClick.AddListener(HandleResetKeybindsClicked);
        }
    }

    private void UnhookControls()
    {
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.RemoveListener(HandleMasterVolumeChanged);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.RemoveListener(HandleMusicVolumeChanged);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.RemoveListener(HandleSfxVolumeChanged);
        }

        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.onValueChanged.RemoveListener(HandleMouseSensitivityChanged);
        }

        if (invertYToggle != null)
        {
            invertYToggle.onValueChanged.RemoveListener(HandleInvertYChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.RemoveListener(HandleFullscreenChanged);
        }

        if (targetFrameRateDropdown != null)
        {
            targetFrameRateDropdown.onValueChanged.RemoveListener(HandleTargetFrameRateDropdownChanged);
        }

        if (resetAccountSettingsButton != null)
        {
            resetAccountSettingsButton.onClick.RemoveListener(HandleResetAccountSettingsClicked);
        }

        if (resetKeybindsButton != null)
        {
            resetKeybindsButton.onClick.RemoveListener(HandleResetKeybindsClicked);
        }
    }

    private void HandleMasterVolumeChanged(float value)
    {
        if (_isRefreshing || _settingsService == null)
        {
            return;
        }

        _settingsService.SetMasterVolume(Mathf.Clamp01(value));
        RefreshValueTexts();
        SetStatus(saveImmediately ? "Master volume saved." : "Master volume changed.");
    }

    private void HandleMusicVolumeChanged(float value)
    {
        if (_isRefreshing || _settingsService == null || _settingsService.AccountSettings == null)
        {
            return;
        }

        _settingsService.AccountSettings.musicVolume = Mathf.Clamp01(value);

        if (saveImmediately)
        {
            _settingsService.SaveAll();
        }

        RefreshValueTexts();
        SetStatus(saveImmediately ? "Music volume saved." : "Music volume changed.");
    }

    private void HandleSfxVolumeChanged(float value)
    {
        if (_isRefreshing || _settingsService == null || _settingsService.AccountSettings == null)
        {
            return;
        }

        _settingsService.AccountSettings.sfxVolume = Mathf.Clamp01(value);

        if (saveImmediately)
        {
            _settingsService.SaveAll();
        }

        RefreshValueTexts();
        SetStatus(saveImmediately ? "SFX volume saved." : "SFX volume changed.");
    }

    private void HandleMouseSensitivityChanged(float value)
    {
        if (_isRefreshing || _settingsService == null)
        {
            return;
        }

        _settingsService.SetMouseSensitivity(value);
        RefreshValueTexts();
        SetStatus(saveImmediately ? "Mouse sensitivity saved." : "Mouse sensitivity changed.");
    }

    private void HandleInvertYChanged(bool value)
    {
        if (_isRefreshing || _settingsService == null)
        {
            return;
        }

        _settingsService.SetInvertY(value);
        SetStatus(saveImmediately ? "Invert Y saved." : "Invert Y changed.");
    }

    private void HandleFullscreenChanged(bool value)
    {
        if (_isRefreshing || _settingsService == null || _settingsService.AccountSettings == null)
        {
            return;
        }

        _settingsService.AccountSettings.fullscreen = value;
        Screen.fullScreen = value;

        if (saveImmediately)
        {
            _settingsService.SaveAll();
        }

        SetStatus(saveImmediately ? "Fullscreen setting saved." : "Fullscreen changed.");
    }

    private void HandleTargetFrameRateDropdownChanged(int dropdownIndex)
    {
        if (_isRefreshing || _settingsService == null || _settingsService.AccountSettings == null)
        {
            return;
        }

        int targetFrameRate = ReadTargetFrameRateFromDropdown(dropdownIndex);

        _settingsService.AccountSettings.targetFrameRate = targetFrameRate;
        Application.targetFrameRate = targetFrameRate;

        if (saveImmediately)
        {
            _settingsService.SaveAll();
        }

        SetStatus(targetFrameRate <= 0 ? "Frame rate set to unlimited." : $"Frame rate set to {targetFrameRate}.");
    }

    private void HandleResetAccountSettingsClicked()
    {
        if (_settingsService == null)
        {
            return;
        }

        _settingsService.ResetAccountSettingsToDefault();
        Refresh();
        SetStatus("Account settings reset to defaults.");
    }

    private void HandleResetKeybindsClicked()
    {
        if (_settingsService == null)
        {
            return;
        }

        _settingsService.ResetKeybindsToDefault();
        Refresh();
        SetStatus("Keybinds reset to defaults.");
    }

    private void RefreshValueTexts()
    {
        AccountSettingsData settings = _settingsService != null ? _settingsService.AccountSettings : null;

        if (settings == null)
        {
            return;
        }

        if (masterVolumeValueText != null)
        {
            masterVolumeValueText.text = FormatPercent(settings.masterVolume);
        }

        if (musicVolumeValueText != null)
        {
            musicVolumeValueText.text = FormatPercent(settings.musicVolume);
        }

        if (sfxVolumeValueText != null)
        {
            sfxVolumeValueText.text = FormatPercent(settings.sfxVolume);
        }

        if (mouseSensitivityValueText != null)
        {
            mouseSensitivityValueText.text = $"{settings.mouseSensitivity:0.00}x";
        }
    }

    private void SelectFrameRateDropdownValue(int targetFrameRate)
    {
        if (targetFrameRateDropdown == null)
        {
            return;
        }

        for (int i = 0; i < targetFrameRateDropdown.options.Count; i++)
        {
            string optionText = targetFrameRateDropdown.options[i].text;

            if (TryParseFrameRateOption(optionText, out int parsedFrameRate) &&
                parsedFrameRate == targetFrameRate)
            {
                targetFrameRateDropdown.value = i;
                return;
            }
        }

        targetFrameRateDropdown.value = 0;
    }

    private int ReadTargetFrameRateFromDropdown(int dropdownIndex)
    {
        if (targetFrameRateDropdown == null ||
            dropdownIndex < 0 ||
            dropdownIndex >= targetFrameRateDropdown.options.Count)
        {
            return -1;
        }

        string optionText = targetFrameRateDropdown.options[dropdownIndex].text;

        return TryParseFrameRateOption(optionText, out int parsedFrameRate)
            ? parsedFrameRate
            : -1;
    }

    private static bool TryParseFrameRateOption(string optionText, out int targetFrameRate)
    {
        targetFrameRate = -1;

        if (string.IsNullOrWhiteSpace(optionText))
        {
            return true;
        }

        string normalized = optionText.Trim().ToLowerInvariant();

        if (normalized.Contains("unlimited") || normalized.Contains("default"))
        {
            targetFrameRate = -1;
            return true;
        }

        string digits = string.Empty;

        for (int i = 0; i < normalized.Length; i++)
        {
            if (char.IsDigit(normalized[i]))
            {
                digits += normalized[i];
            }
        }

        return int.TryParse(digits, out targetFrameRate);
    }

    private static void SetSlider(Slider slider, float value, float min, float max)
    {
        if (slider == null)
        {
            return;
        }

        slider.minValue = min;
        slider.maxValue = max;
        slider.value = Mathf.Clamp(value, min, max);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message ?? string.Empty;
        }
    }

    private static string FormatPercent(float value)
    {
        return $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";
    }
}
