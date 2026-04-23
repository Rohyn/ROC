using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Persistent controller that connects the local player's interaction selector
/// to a screen-space interaction prompt.
///
/// RESPONSIBILITIES:
/// - find the local player's PlayerInteractionSelector
/// - subscribe to target changes
/// - show / hide the prompt
/// - update prompt position every frame while a target exists
///
/// This is designed to live on a persistent object such as AppRoot or UIRoot.
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
    }

    private void OnDisable()
    {
        UnbindSelector();
    }

    private void Update()
    {
        // If we do not currently have a selector, keep retrying periodically.
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

        // If the selector exists but the current target is null, hide the prompt.
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

    /// <summary>
    /// Attempts to find and bind to the local player's interaction selector.
    /// </summary>
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

    /// <summary>
    /// Immediately updates prompt visibility/text based on the current target.
    /// Position is still maintained every frame afterward.
    /// </summary>
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

	    promptView.Show($"E - {_currentTarget.InteractionPrompt}");
	    UpdatePromptPosition();
	}

    /// <summary>
    /// Updates the prompt's screen position so it stays near the interactable's focus point.
    /// </summary>
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

	    Vector3 worldPosition = _currentTarget.InteractionFocusPosition + worldOffset;
	    Vector3 screenPosition = worldCamera.WorldToScreenPoint(worldPosition);

	    // If the point is behind the camera, hide the prompt.
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