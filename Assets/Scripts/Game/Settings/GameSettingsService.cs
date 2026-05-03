using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime settings service for ROC.
/// 
/// v0 responsibilities:
/// - load/save account-level settings JSON
/// - load/save active character-level settings JSON shell
/// - provide effective keybind lookup
/// - provide mouse sensitivity / invert Y lookup
/// - provide reset-to-defaults helpers
/// 
/// Put one on AppRoot, or let GetOrCreate() create one automatically.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameSettingsService : MonoBehaviour
{
    private const string SettingsDirectoryName = "Settings";
    private const string AccountSettingsFileName = "AccountSettings.json";
    private const string CharacterSettingsDirectoryName = "CharacterSettings";

    private static GameSettingsService _instance;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    public static GameSettingsService Instance => GetOrCreate();

    public AccountSettingsData AccountSettings { get; private set; }
    public CharacterSettingsData ActiveCharacterSettings { get; private set; }
    public string ActiveCharacterId { get; private set; } = string.Empty;

    public event Action SettingsChanged;
    public event Action KeybindsChanged;

    private string SettingsDirectoryPath =>
        Path.Combine(Application.persistentDataPath, SettingsDirectoryName);

    private string AccountSettingsPath =>
        Path.Combine(SettingsDirectoryPath, AccountSettingsFileName);

    private string CharacterSettingsDirectoryPath =>
        Path.Combine(SettingsDirectoryPath, CharacterSettingsDirectoryName);

    public static GameSettingsService GetOrCreate()
    {
        if (_instance != null)
        {
            return _instance;
        }

        _instance = FindFirstObjectByType<GameSettingsService>();

        if (_instance != null)
        {
            return _instance;
        }

        GameObject serviceObject = new GameObject("GameSettingsService");
        _instance = serviceObject.AddComponent<GameSettingsService>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        LoadAccountSettings();

        if (string.IsNullOrWhiteSpace(ActiveCharacterId))
        {
            ActiveCharacterSettings = null;
        }

        ApplyRuntimeSettings();
    }

    public void SetActiveCharacterId(string characterId)
    {
        string normalizedCharacterId = string.IsNullOrWhiteSpace(characterId)
            ? string.Empty
            : characterId.Trim();

        if (ActiveCharacterId == normalizedCharacterId && ActiveCharacterSettings != null)
        {
            return;
        }

        if (ActiveCharacterSettings != null)
        {
            SaveActiveCharacterSettings();
        }

        ActiveCharacterId = normalizedCharacterId;

        if (string.IsNullOrWhiteSpace(ActiveCharacterId))
        {
            ActiveCharacterSettings = null;
        }
        else
        {
            LoadCharacterSettings(ActiveCharacterId);
        }

        RaiseChanged();
    }

    public Key GetEffectiveKey(string actionId, Key fallback = Key.None)
    {
        EnsureLoaded();

        if (ActiveCharacterSettings != null &&
            ActiveCharacterSettings.TryGetOverrideKey(actionId, out Key overrideKey))
        {
            return overrideKey;
        }

        return AccountSettings != null
            ? AccountSettings.GetKey(actionId, fallback)
            : fallback;
    }

    public bool RebindAccountKey(string actionId, Key key)
    {
        if (string.IsNullOrWhiteSpace(actionId) || key == Key.None)
        {
            return false;
        }

        EnsureLoaded();

        AccountSettings.SetKey(actionId.Trim(), key);
        AccountSettings.EnsureValid();

        SaveAccountSettings();
        RaiseChanged(keybindsChanged: true);
        return true;
    }

    public bool RebindCharacterKey(string actionId, Key key)
    {
        if (string.IsNullOrWhiteSpace(actionId) || key == Key.None)
        {
            return false;
        }

        if (ActiveCharacterSettings == null)
        {
            return false;
        }

        ActiveCharacterSettings.useCharacterSpecificKeybinds = true;
        ActiveCharacterSettings.SetOverrideKey(actionId.Trim(), key);
        ActiveCharacterSettings.EnsureValid();

        SaveActiveCharacterSettings();
        RaiseChanged(keybindsChanged: true);
        return true;
    }

    public void SetMouseSensitivity(float value)
    {
        EnsureLoaded();

        AccountSettings.mouseSensitivity = Mathf.Clamp(value, 0.05f, 5f);
        SaveAccountSettings();
        RaiseChanged();
    }

    public void SetInvertY(bool value)
    {
        EnsureLoaded();

        AccountSettings.invertY = value;
        SaveAccountSettings();
        RaiseChanged();
    }

    public void SetMasterVolume(float value)
    {
        EnsureLoaded();

        AccountSettings.masterVolume = Mathf.Clamp01(value);
        ApplyRuntimeSettings();
        SaveAccountSettings();
        RaiseChanged();
    }

    public void ResetAccountSettingsToDefault()
    {
        AccountSettings = AccountSettingsData.CreateDefault();
        SaveAccountSettings();
        ApplyRuntimeSettings();
        RaiseChanged(keybindsChanged: true);
    }

    public void ResetKeybindsToDefault()
    {
        EnsureLoaded();

        AccountSettings.ResetKeybindsToDefault();
        SaveAccountSettings();
        RaiseChanged(keybindsChanged: true);
    }

    public void ResetActiveCharacterSettingsToDefault()
    {
        if (string.IsNullOrWhiteSpace(ActiveCharacterId))
        {
            return;
        }

        ActiveCharacterSettings = CharacterSettingsData.CreateDefault(ActiveCharacterId);
        SaveActiveCharacterSettings();
        RaiseChanged(keybindsChanged: true);
    }

    public void SaveAll()
    {
        SaveAccountSettings();
        SaveActiveCharacterSettings();
    }

    private void EnsureLoaded()
    {
        if (AccountSettings == null)
        {
            LoadAccountSettings();
        }
    }

    private void LoadAccountSettings()
    {
        Directory.CreateDirectory(SettingsDirectoryPath);

        if (!File.Exists(AccountSettingsPath))
        {
            AccountSettings = AccountSettingsData.CreateDefault();
            SaveAccountSettings();

            if (verboseLogging)
            {
                Debug.Log($"[GameSettingsService] Created default account settings at {AccountSettingsPath}", this);
            }

            return;
        }

        try
        {
            string json = File.ReadAllText(AccountSettingsPath);
            AccountSettings = JsonUtility.FromJson<AccountSettingsData>(json) ?? AccountSettingsData.CreateDefault();
            AccountSettings.EnsureValid();

            if (verboseLogging)
            {
                Debug.Log($"[GameSettingsService] Loaded account settings from {AccountSettingsPath}", this);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GameSettingsService] Failed to load account settings. Using defaults. {exception}", this);
            AccountSettings = AccountSettingsData.CreateDefault();
        }
    }

    private void SaveAccountSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectoryPath);

            if (AccountSettings == null)
            {
                AccountSettings = AccountSettingsData.CreateDefault();
            }

            AccountSettings.EnsureValid();

            string json = JsonUtility.ToJson(AccountSettings, prettyPrint: true);
            File.WriteAllText(AccountSettingsPath, json);

            if (verboseLogging)
            {
                Debug.Log($"[GameSettingsService] Saved account settings to {AccountSettingsPath}", this);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GameSettingsService] Failed to save account settings. {exception}", this);
        }
    }

    private void LoadCharacterSettings(string characterId)
    {
        Directory.CreateDirectory(CharacterSettingsDirectoryPath);

        string path = GetCharacterSettingsPath(characterId);

        if (!File.Exists(path))
        {
            ActiveCharacterSettings = CharacterSettingsData.CreateDefault(characterId);
            SaveActiveCharacterSettings();

            if (verboseLogging)
            {
                Debug.Log($"[GameSettingsService] Created default character settings at {path}", this);
            }

            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            ActiveCharacterSettings = JsonUtility.FromJson<CharacterSettingsData>(json) ??
                                      CharacterSettingsData.CreateDefault(characterId);

            ActiveCharacterSettings.characterId = characterId;
            ActiveCharacterSettings.EnsureValid();

            if (verboseLogging)
            {
                Debug.Log($"[GameSettingsService] Loaded character settings from {path}", this);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GameSettingsService] Failed to load character settings. Using defaults. {exception}", this);
            ActiveCharacterSettings = CharacterSettingsData.CreateDefault(characterId);
        }
    }

    private void SaveActiveCharacterSettings()
    {
        if (ActiveCharacterSettings == null || string.IsNullOrWhiteSpace(ActiveCharacterId))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CharacterSettingsDirectoryPath);

            ActiveCharacterSettings.characterId = ActiveCharacterId;
            ActiveCharacterSettings.EnsureValid();

            string json = JsonUtility.ToJson(ActiveCharacterSettings, prettyPrint: true);
            File.WriteAllText(GetCharacterSettingsPath(ActiveCharacterId), json);

            if (verboseLogging)
            {
                Debug.Log($"[GameSettingsService] Saved character settings for '{ActiveCharacterId}'.", this);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GameSettingsService] Failed to save character settings. {exception}", this);
        }
    }

    private string GetCharacterSettingsPath(string characterId)
    {
        string safeCharacterId = MakeSafeFileName(characterId);
        return Path.Combine(CharacterSettingsDirectoryPath, $"{safeCharacterId}.json");
    }

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        string result = value.Trim();

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalidChar, '_');
        }

        return result;
    }

    private void ApplyRuntimeSettings()
    {
        if (AccountSettings == null)
        {
            return;
        }

        AudioListener.volume = Mathf.Clamp01(AccountSettings.masterVolume);

        if (AccountSettings.targetFrameRate != 0)
        {
            Application.targetFrameRate = AccountSettings.targetFrameRate;
        }

        Screen.fullScreen = AccountSettings.fullscreen;
    }

    private void RaiseChanged(bool keybindsChanged = false)
    {
        SettingsChanged?.Invoke();

        if (keybindsChanged)
        {
            KeybindsChanged?.Invoke();
        }
    }
}
