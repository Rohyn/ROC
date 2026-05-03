using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Account-level settings.
/// Use this for preferences that should generally follow the player across characters:
/// audio, graphics, mouse/camera preferences, and default keybinds.
/// </summary>
[Serializable]
public class AccountSettingsData
{
    public int schemaVersion = 1;

    [Header("Audio")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Camera / Look")]
    [Range(0.05f, 5f)] public float mouseSensitivity = 1f;
    public bool invertY = false;

    [Header("Graphics Placeholders")]
    public bool fullscreen = true;
    public int targetFrameRate = -1;

    [Header("Default Keybinds")]
    public List<KeybindEntry> keybinds = new List<KeybindEntry>();

    public static AccountSettingsData CreateDefault()
    {
        AccountSettingsData data = new AccountSettingsData();
        data.ResetKeybindsToDefault();
        return data;
    }

    public void EnsureValid()
    {
        schemaVersion = Mathf.Max(1, schemaVersion);

        masterVolume = Mathf.Clamp01(masterVolume);
        musicVolume = Mathf.Clamp01(musicVolume);
        sfxVolume = Mathf.Clamp01(sfxVolume);

        mouseSensitivity = Mathf.Clamp(mouseSensitivity, 0.05f, 5f);

        if (keybinds == null)
        {
            keybinds = new List<KeybindEntry>();
        }

        EnsureDefaultKeybindExists(KeybindActionId.MoveForward, Key.W);
        EnsureDefaultKeybindExists(KeybindActionId.MoveBackward, Key.S);
        EnsureDefaultKeybindExists(KeybindActionId.MoveLeft, Key.A);
        EnsureDefaultKeybindExists(KeybindActionId.MoveRight, Key.D);

        EnsureDefaultKeybindExists(KeybindActionId.Jump, Key.Space);
        EnsureDefaultKeybindExists(KeybindActionId.Interact, Key.E);
        EnsureDefaultKeybindExists(KeybindActionId.Cancel, Key.Escape);

        EnsureDefaultKeybindExists(KeybindActionId.ToggleFreeCursor, Key.Period);
        EnsureDefaultKeybindExists(KeybindActionId.ToggleMenu, Key.Tab);
        EnsureDefaultKeybindExists(KeybindActionId.ToggleInventory, Key.I);
        EnsureDefaultKeybindExists(KeybindActionId.ToggleJournal, Key.J);
        EnsureDefaultKeybindExists(KeybindActionId.TogglePerspective, Key.V);
    }

    public void ResetKeybindsToDefault()
    {
        keybinds = new List<KeybindEntry>
        {
            new KeybindEntry(KeybindActionId.MoveForward, Key.W),
            new KeybindEntry(KeybindActionId.MoveBackward, Key.S),
            new KeybindEntry(KeybindActionId.MoveLeft, Key.A),
            new KeybindEntry(KeybindActionId.MoveRight, Key.D),

            new KeybindEntry(KeybindActionId.Jump, Key.Space),
            new KeybindEntry(KeybindActionId.Interact, Key.E),
            new KeybindEntry(KeybindActionId.Cancel, Key.Escape),

            new KeybindEntry(KeybindActionId.ToggleFreeCursor, Key.Period),
            new KeybindEntry(KeybindActionId.ToggleMenu, Key.Tab),
            new KeybindEntry(KeybindActionId.ToggleInventory, Key.I),
            new KeybindEntry(KeybindActionId.ToggleJournal, Key.J),
            new KeybindEntry(KeybindActionId.TogglePerspective, Key.V),
        };
    }

    public Key GetKey(string actionId, Key fallback = Key.None)
    {
        KeybindEntry entry = FindEntry(actionId);
        return entry != null ? entry.GetKey(fallback) : fallback;
    }

    public void SetKey(string actionId, Key key)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        KeybindEntry entry = FindEntry(actionId);

        if (entry == null)
        {
            keybinds.Add(new KeybindEntry(actionId.Trim(), key));
            return;
        }

        entry.SetKey(key);
    }

    private KeybindEntry FindEntry(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId) || keybinds == null)
        {
            return null;
        }

        string normalizedActionId = actionId.Trim();

        for (int i = 0; i < keybinds.Count; i++)
        {
            KeybindEntry entry = keybinds[i];

            if (entry == null || string.IsNullOrWhiteSpace(entry.actionId))
            {
                continue;
            }

            if (entry.actionId.Trim() == normalizedActionId)
            {
                return entry;
            }
        }

        return null;
    }

    private void EnsureDefaultKeybindExists(string actionId, Key key)
    {
        if (FindEntry(actionId) != null)
        {
            return;
        }

        keybinds.Add(new KeybindEntry(actionId, key));
    }
}
