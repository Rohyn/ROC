using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Small input helper for ROC's first configurable-keybind pass.
/// This intentionally stays simple and keyboard-focused for v0.
/// </summary>
public static class RocInput
{
    public static bool WasPressedThisFrame(string actionId, Key fallback = Key.None)
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return false;
        }

        Key key = GetKey(actionId, fallback);

        if (key == Key.None)
        {
            return false;
        }

        KeyControl control = keyboard[key];
        return control != null && control.wasPressedThisFrame;
    }

    public static bool IsPressed(string actionId, Key fallback = Key.None)
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return false;
        }

        Key key = GetKey(actionId, fallback);

        if (key == Key.None)
        {
            return false;
        }

        KeyControl control = keyboard[key];
        return control != null && control.isPressed;
    }

    public static bool WasReleasedThisFrame(string actionId, Key fallback = Key.None)
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return false;
        }

        Key key = GetKey(actionId, fallback);

        if (key == Key.None)
        {
            return false;
        }

        KeyControl control = keyboard[key];
        return control != null && control.wasReleasedThisFrame;
    }

    public static Vector2 GetMovementVector()
    {
        float x = 0f;
        float y = 0f;

        if (IsPressed(KeybindActionId.MoveLeft, Key.A))
        {
            x -= 1f;
        }

        if (IsPressed(KeybindActionId.MoveRight, Key.D))
        {
            x += 1f;
        }

        if (IsPressed(KeybindActionId.MoveBackward, Key.S))
        {
            y -= 1f;
        }

        if (IsPressed(KeybindActionId.MoveForward, Key.W))
        {
            y += 1f;
        }

        Vector2 movement = new Vector2(x, y);
        return movement.sqrMagnitude > 1f ? movement.normalized : movement;
    }

    public static Key GetKey(string actionId, Key fallback = Key.None)
    {
        return GameSettingsService.Instance.GetEffectiveKey(actionId, fallback);
    }

    public static string GetDisplayName(string actionId, Key fallback = Key.None)
    {
        Keyboard keyboard = Keyboard.current;
        Key key = GetKey(actionId, fallback);

        if (key == Key.None)
        {
            return "Unbound";
        }

        if (keyboard != null)
        {
            KeyControl control = keyboard[key];

            if (control != null && !string.IsNullOrWhiteSpace(control.displayName))
            {
                return control.displayName;
            }
        }

        return key.ToString();
    }

    public static bool TryGetAnyPressedKeyThisFrame(out Key key)
    {
        key = Key.None;

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return false;
        }

        for (int i = 0; i < keyboard.allKeys.Count; i++)
        {
            KeyControl control = keyboard.allKeys[i];

            if (control == null || !control.wasPressedThisFrame)
            {
                continue;
            }

            key = control.keyCode;
            return key != Key.None;
        }

        return false;
    }
}
