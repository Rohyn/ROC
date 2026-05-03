using System;
using UnityEngine.InputSystem;

/// <summary>
/// Serializable keybind row.
/// JsonUtility does not serialize dictionaries, so settings store keybinds as lists.
/// </summary>
[Serializable]
public class KeybindEntry
{
    public string actionId;
    public string keyName;

    public KeybindEntry()
    {
        actionId = string.Empty;
        keyName = Key.None.ToString();
    }

    public KeybindEntry(string actionId, Key key)
    {
        this.actionId = actionId ?? string.Empty;
        keyName = key.ToString();
    }

    public Key GetKey(Key fallback = Key.None)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return fallback;
        }

        return Enum.TryParse(keyName.Trim(), ignoreCase: true, out Key parsed)
            ? parsed
            : fallback;
    }

    public void SetKey(Key key)
    {
        keyName = key.ToString();
    }
}
