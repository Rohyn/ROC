using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Account-only keybind rebind button for the main menu settings screen.
/// 
/// Add this to a UI Button row. Assign Action Id, Fallback Key, Label Prefix, and Label Text.
/// Click the button in Play Mode, then press a key.
/// Escape cancels rebinding.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class AccountKeybindRebindButton : MonoBehaviour
{
    [Header("Binding")]
    [SerializeField] private string actionId = KeybindActionId.Interact;
    [SerializeField] private Key fallbackKey = Key.E;

    [Header("UI")]
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private string labelPrefix = "Interact";

    [Header("Behavior")]
    [SerializeField] private bool allowEscapeAsBinding = false;

    private Button _button;
    private bool _isListening;
    private GameSettingsService _settingsService;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        _settingsService = GameSettingsService.GetOrCreate();

        if (_button != null)
        {
            _button.onClick.AddListener(BeginListening);
        }

        _settingsService.KeybindsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (_button != null)
        {
            _button.onClick.RemoveListener(BeginListening);
        }

        if (_settingsService != null)
        {
            _settingsService.KeybindsChanged -= Refresh;
        }

        _settingsService = null;
        _isListening = false;
    }

    private void Update()
    {
        if (!_isListening)
        {
            return;
        }

        if (!RocInput.TryGetAnyPressedKeyThisFrame(out Key key))
        {
            return;
        }

        if (key == Key.Escape && !allowEscapeAsBinding)
        {
            _isListening = false;
            Refresh();
            return;
        }

        bool success = GameSettingsService.Instance.RebindAccountKey(actionId, key);
        _isListening = false;

        if (!success)
        {
            Debug.LogWarning($"[AccountKeybindRebindButton] Failed to bind action '{actionId}' to key '{key}'.", this);
        }

        Refresh();
    }

    public void BeginListening()
    {
        _isListening = true;

        if (labelText != null)
        {
            labelText.text = $"{labelPrefix}: press a key...";
        }
    }

    public void Refresh()
    {
        if (labelText == null)
        {
            return;
        }

        labelText.text = $"{labelPrefix}: {RocInput.GetDisplayName(actionId, fallbackKey)}";
    }
}
