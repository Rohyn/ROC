using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Temporary UI helper for rebinding one action.
/// Add it to a button row, assign Action Id and Label Text, then click in Play Mode
/// and press a key.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class KeybindRebindButton : MonoBehaviour
{
    [Header("Binding")]
    [SerializeField] private string actionId = KeybindActionId.Interact;
    [SerializeField] private Key fallbackKey = Key.E;

    [Tooltip("If true and an active character settings file is loaded, this saves as a character override.")]
    [SerializeField] private bool characterSpecific = false;

    [Header("UI")]
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private string labelPrefix = "Interact";

    private Button _button;
    private bool _isListening;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (_button != null)
        {
            _button.onClick.AddListener(BeginListening);
        }

        GameSettingsService.Instance.KeybindsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (_button != null)
        {
            _button.onClick.RemoveListener(BeginListening);
        }

        GameSettingsService settingsService = GameSettingsService.GetOrCreate();

        if (settingsService != null)
        {
            settingsService.KeybindsChanged -= Refresh;
        }

        _isListening = false;
    }

    private void Update()
    {
        if (!_isListening)
        {
            return;
        }

        if (RocInput.TryGetAnyPressedKeyThisFrame(out Key key))
        {
            if (key == Key.Escape)
            {
                _isListening = false;
                Refresh();
                return;
            }

            bool success = characterSpecific
                ? GameSettingsService.Instance.RebindCharacterKey(actionId, key)
                : GameSettingsService.Instance.RebindAccountKey(actionId, key);

            _isListening = false;

            if (!success)
            {
                Debug.LogWarning($"[KeybindRebindButton] Failed to bind action '{actionId}' to key '{key}'.", this);
            }

            Refresh();
        }
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
