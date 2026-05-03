using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Persistent controller that connects the local player's interaction selector
/// to a screen-space interaction prompt.
/// 
/// This version displays the current account/character keybind for Interact
/// instead of hardcoding "E".
/// </summary>
[DisallowMultipleComponent]
public class InteractionPromptController : MonoBehaviour
{
    [Header("Required References")]
    [Tooltip("The persistent prompt view that should be shown/hidden.")]
    [SerializeField] private InteractionPromptView promptView;

    [Header("World Tracking")]
    [Tooltip("Optional world-space offset applied above the interactable focus position.")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.2f, 0f);

    [Header("Display")]
    [SerializeField] private string promptSeparator = " - ";
    [SerializeField] private Key fallbackInteractKey = Key.E;

    [Header("Selector Binding")]
    [Tooltip("How often to retry finding the local player's selector when not currently bound.")]
    [SerializeField] private float selectorSearchIntervalSeconds = 0.5f;

    [Header("Debug")]
    [Tooltip("If true, selector binding and prompt updates will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerInteractionSelector _boundSelector;
    private GenericInteractable _currentTarget;
    private float _nextSelectorSearchTime;

    private void Awake()
    {
        GameSettingsService.GetOrCreate();

        if (promptView == null)
        {
            promptView = FindFirstObjectByType<InteractionPromptView>();
        }

        if (promptView != null)
        {
            promptView.Hide();
        }
    }

    private void OnEnable()
    {
        TryBindLocalSelector(force: true);
        GameSettingsService.Instance.KeybindsChanged += RefreshPromptImmediate;
    }

    private void OnDisable()
    {
        UnbindSelector();

        if (GameSettingsService.GetOrCreate() != null)
        {
            GameSettingsService.Instance.KeybindsChanged -= RefreshPromptImmediate;
        }
    }

    private void Update()
    {
        if (_boundSelector == null)
        {
            if (Time.time >= _nextSelectorSearchTime)
            {
                TryBindLocalSelector(force: false);
                _nextSelectorSearchTime = Time.time + selectorSearchIntervalSeconds;
            }

            if (promptView != null)
            {
                promptView.Hide();
            }

            return;
        }

        if (_currentTarget == null)
        {
            if (promptView != null)
            {
                promptView.Hide();
            }

            return;
        }

        UpdatePromptPosition();
    }

    private void TryBindLocalSelector(bool force)
    {
        if (!force && _boundSelector != null)
        {
            return;
        }

        PlayerInteractionSelector[] selectors =
            FindObjectsByType<PlayerInteractionSelector>(FindObjectsSortMode.None);

        for (int i = 0; i < selectors.Length; i++)
        {
            PlayerInteractionSelector selector = selectors[i];

            if (selector == null)
            {
                continue;
            }

            if (!selector.IsOwner)
            {
                continue;
            }

            BindSelector(selector);
            return;
        }
    }

    private void BindSelector(PlayerInteractionSelector selector)
    {
        if (selector == _boundSelector)
        {
            return;
        }

        UnbindSelector();

        _boundSelector = selector;
        _boundSelector.CurrentTargetChanged += HandleCurrentTargetChanged;
        _currentTarget = _boundSelector.CurrentTarget;

        if (verboseLogging)
        {
            Debug.Log("[InteractionPromptController] Bound to local PlayerInteractionSelector.");
        }

        RefreshPromptImmediate();
    }

    private void UnbindSelector()
    {
        if (_boundSelector != null)
        {
            _boundSelector.CurrentTargetChanged -= HandleCurrentTargetChanged;
            _boundSelector = null;
        }

        _currentTarget = null;
    }

    private void HandleCurrentTargetChanged(GenericInteractable newTarget)
    {
        _currentTarget = newTarget;

        if (verboseLogging)
        {
            string targetName = _currentTarget != null ? _currentTarget.name : "null";
            Debug.Log($"[InteractionPromptController] Current target changed to '{targetName}'.");
        }

        RefreshPromptImmediate();
    }

    private void RefreshPromptImmediate()
    {
        if (promptView == null)
        {
            return;
        }

        if (_currentTarget == null)
        {
            promptView.Hide();
            return;
        }

        promptView.Show(BuildPromptText());
        UpdatePromptPosition();
    }

    private string BuildPromptText()
    {
        string interactKey = ControlPromptFormatter.GetActionDisplay(
            KeybindActionId.Interact,
            fallbackInteractKey,
            wrapKeysInBrackets: false);

        string interactionText = _currentTarget != null
            ? _currentTarget.InteractionPrompt
            : string.Empty;

        return $"{interactKey}{promptSeparator}{interactionText}";
    }

    private void UpdatePromptPosition()
    {
        if (promptView == null || _currentTarget == null)
        {
            return;
        }

        Camera worldCamera = Camera.main;

        if (worldCamera == null)
        {
            promptView.Hide();
            return;
        }

        Vector3 referencePosition = _boundSelector != null
            ? _boundSelector.transform.position
            : worldCamera.transform.position;

        Vector3 worldPosition =
            _currentTarget.GetBestInteractionFocusPosition(referencePosition) + worldOffset;

        Vector3 screenPosition = worldCamera.WorldToScreenPoint(worldPosition);

        if (screenPosition.z <= 0f)
        {
            promptView.Hide();
            return;
        }

        Camera uiCamera = null;
        Canvas canvas = promptView.GetRootCanvas();

        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCamera = canvas.worldCamera != null ? canvas.worldCamera : worldCamera;
        }

        promptView.SetScreenPosition(screenPosition, uiCamera);
    }
}
