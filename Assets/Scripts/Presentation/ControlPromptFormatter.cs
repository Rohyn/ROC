using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem;

/// <summary>
/// Formats player-facing control prompt text using the current account/character keybinds.
/// 
/// Supported tokens:
/// - {key:interact}
/// - {interact}
/// - {key:menu.toggle}
/// - {menu.toggle}
/// - {menu}
/// - {freecursor}
/// - {inventory}
/// - {journal}
/// - {cancel}
/// 
/// Unknown tokens are left unchanged.
/// </summary>
public static class ControlPromptFormatter
{
    private static readonly Regex TokenRegex = new Regex(
        @"\{(?:key:)?([a-zA-Z0-9_.-]+)\}",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, string> AliasToActionId = new Dictionary<string, string>
    {
        { "moveforward", KeybindActionId.MoveForward },
        { "move.forward", KeybindActionId.MoveForward },
        { "forward", KeybindActionId.MoveForward },

        { "movebackward", KeybindActionId.MoveBackward },
        { "move.backward", KeybindActionId.MoveBackward },
        { "backward", KeybindActionId.MoveBackward },
        { "back", KeybindActionId.MoveBackward },

        { "moveleft", KeybindActionId.MoveLeft },
        { "move.left", KeybindActionId.MoveLeft },
        { "left", KeybindActionId.MoveLeft },

        { "moveright", KeybindActionId.MoveRight },
        { "move.right", KeybindActionId.MoveRight },
        { "right", KeybindActionId.MoveRight },

        { "jump", KeybindActionId.Jump },

        { "interact", KeybindActionId.Interact },
        { "use", KeybindActionId.Interact },

        { "cancel", KeybindActionId.Cancel },
        { "close", KeybindActionId.Cancel },
        { "backout", KeybindActionId.Cancel },

        { "cursor.toggle_free", KeybindActionId.ToggleFreeCursor },
        { "togglefreecursor", KeybindActionId.ToggleFreeCursor },
        { "freecursor", KeybindActionId.ToggleFreeCursor },
        { "cursor", KeybindActionId.ToggleFreeCursor },

        { "menu.toggle", KeybindActionId.ToggleMenu },
        { "togglemenu", KeybindActionId.ToggleMenu },
        { "menu", KeybindActionId.ToggleMenu },

        { "inventory.toggle", KeybindActionId.ToggleInventory },
        { "toggleinventory", KeybindActionId.ToggleInventory },
        { "inventory", KeybindActionId.ToggleInventory },

        { "journal.toggle", KeybindActionId.ToggleJournal },
        { "togglejournal", KeybindActionId.ToggleJournal },
        { "journal", KeybindActionId.ToggleJournal },

        { "camera.toggle_perspective", KeybindActionId.TogglePerspective },
        { "toggleperspective", KeybindActionId.TogglePerspective },
        { "perspective", KeybindActionId.TogglePerspective }
    };

    private static readonly Dictionary<string, Key> FallbackKeysByActionId = new Dictionary<string, Key>
    {
        { KeybindActionId.MoveForward, Key.W },
        { KeybindActionId.MoveBackward, Key.S },
        { KeybindActionId.MoveLeft, Key.A },
        { KeybindActionId.MoveRight, Key.D },

        { KeybindActionId.Jump, Key.Space },
        { KeybindActionId.Interact, Key.E },
        { KeybindActionId.Cancel, Key.Escape },

        { KeybindActionId.ToggleFreeCursor, Key.Period },
        { KeybindActionId.ToggleMenu, Key.Tab },
        { KeybindActionId.ToggleInventory, Key.I },
        { KeybindActionId.ToggleJournal, Key.J },
        { KeybindActionId.TogglePerspective, Key.V }
    };

    public static string Format(string rawText, bool wrapKeysInBrackets = true)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        return TokenRegex.Replace(rawText, match =>
        {
            string token = match.Groups.Count > 1 ? match.Groups[1].Value : string.Empty;

            if (!TryResolveActionId(token, out string actionId))
            {
                return match.Value;
            }

            return GetActionDisplay(actionId, GetFallbackKey(actionId), wrapKeysInBrackets);
        });
    }

    public static string GetActionDisplay(
        string actionId,
        Key fallbackKey = Key.None,
        bool wrapKeysInBrackets = false)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return string.Empty;
        }

        Key fallback = fallbackKey != Key.None
            ? fallbackKey
            : GetFallbackKey(actionId);

        string displayName = RocInput.GetDisplayName(actionId.Trim(), fallback);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = fallback != Key.None ? fallback.ToString() : "Unbound";
        }

        return wrapKeysInBrackets ? $"[{displayName}]" : displayName;
    }

    public static bool TryResolveActionId(string tokenOrActionId, out string actionId)
    {
        actionId = string.Empty;

        if (string.IsNullOrWhiteSpace(tokenOrActionId))
        {
            return false;
        }

        string trimmed = tokenOrActionId.Trim();
        string normalized = NormalizeToken(trimmed);

        if (AliasToActionId.TryGetValue(normalized, out string mappedActionId))
        {
            actionId = mappedActionId;
            return true;
        }

        if (FallbackKeysByActionId.ContainsKey(trimmed))
        {
            actionId = trimmed;
            return true;
        }

        return false;
    }

    public static Key GetFallbackKey(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return Key.None;
        }

        return FallbackKeysByActionId.TryGetValue(actionId.Trim(), out Key key)
            ? key
            : Key.None;
    }

    private static string NormalizeToken(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
    }
}
