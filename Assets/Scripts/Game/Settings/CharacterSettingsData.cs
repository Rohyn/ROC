using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Character-level settings shell.
/// Use this later for HUD layout, character-specific keybind overrides,
/// role-specific UI preferences, etc.
/// </summary>
[Serializable]
public class CharacterSettingsData
{
    public int schemaVersion = 1;
    public string characterId = string.Empty;

    public bool useCharacterSpecificKeybinds = false;
    public List<KeybindEntry> keybindOverrides = new List<KeybindEntry>();

    public List<HudElementLayoutEntry> hudLayout = new List<HudElementLayoutEntry>();

    public static CharacterSettingsData CreateDefault(string characterId)
    {
        return new CharacterSettingsData
        {
            schemaVersion = 1,
            characterId = characterId ?? string.Empty,
            useCharacterSpecificKeybinds = false,
            keybindOverrides = new List<KeybindEntry>(),
            hudLayout = new List<HudElementLayoutEntry>()
        };
    }

    public void EnsureValid()
    {
        if (keybindOverrides == null)
        {
            keybindOverrides = new List<KeybindEntry>();
        }

        if (hudLayout == null)
        {
            hudLayout = new List<HudElementLayoutEntry>();
        }
    }

    public bool TryGetOverrideKey(string actionId, out Key key)
    {
        key = Key.None;

        if (!useCharacterSpecificKeybinds ||
            string.IsNullOrWhiteSpace(actionId) ||
            keybindOverrides == null)
        {
            return false;
        }

        string normalizedActionId = actionId.Trim();

        for (int i = 0; i < keybindOverrides.Count; i++)
        {
            KeybindEntry entry = keybindOverrides[i];

            if (entry == null || string.IsNullOrWhiteSpace(entry.actionId))
            {
                continue;
            }

            if (entry.actionId.Trim() != normalizedActionId)
            {
                continue;
            }

            key = entry.GetKey(Key.None);
            return key != Key.None;
        }

        return false;
    }

    public void SetOverrideKey(string actionId, Key key)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (keybindOverrides == null)
        {
            keybindOverrides = new List<KeybindEntry>();
        }

        string normalizedActionId = actionId.Trim();

        for (int i = 0; i < keybindOverrides.Count; i++)
        {
            KeybindEntry entry = keybindOverrides[i];

            if (entry == null || string.IsNullOrWhiteSpace(entry.actionId))
            {
                continue;
            }

            if (entry.actionId.Trim() == normalizedActionId)
            {
                entry.SetKey(key);
                return;
            }
        }

        keybindOverrides.Add(new KeybindEntry(normalizedActionId, key));
    }
}

[Serializable]
public class HudElementLayoutEntry
{
    public string elementId;
    public bool isVisible = true;

    public float anchorMinX;
    public float anchorMinY;
    public float anchorMaxX;
    public float anchorMaxY;

    public float pivotX = 0.5f;
    public float pivotY = 0.5f;

    public float anchoredPositionX;
    public float anchoredPositionY;

    public float scale = 1f;
}
