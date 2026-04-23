using TMPro;
using UnityEngine;

/// <summary>
/// Simple view/controller for a persistent interaction prompt on a screen-space canvas.
///
/// RESPONSIBILITIES:
/// - show / hide the prompt
/// - set its text
/// - position it on the canvas using a screen-space point
///
/// Attach this to the TMP_Text object used for the prompt.
/// </summary>
[DisallowMultipleComponent]
public class InteractionPromptView : MonoBehaviour
{
    [Header("Required References")]
    [Tooltip("The root canvas that contains this prompt.")]
    [SerializeField] private Canvas rootCanvas;

    [Tooltip("The TMP text used to display the prompt.")]
    [SerializeField] private TMP_Text promptText;

    [Header("Positioning")]
    [Tooltip("Optional pixel offset applied after converting the target point to canvas space.")]
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 40f);

    [Header("Visibility")]
    [Tooltip("If true, a CanvasGroup will be used for visibility control when available.")]
    [SerializeField] private bool useCanvasGroupIfPresent = true;

    private RectTransform _rectTransform;
    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();

        if (promptText == null)
        {
            promptText = GetComponent<TMP_Text>();
        }

        if (rootCanvas == null)
        {
            rootCanvas = GetComponentInParent<Canvas>();
        }

        if (useCanvasGroupIfPresent)
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        Hide();
    }

    /// <summary>
    /// Shows the prompt with the specified display text.
    /// </summary>
    public void Show(string displayText)
    {
        if (promptText != null)
        {
            promptText.text = displayText;
        }

        SetVisible(true);
    }

    /// <summary>
    /// Hides the prompt.
    /// </summary>
    public void Hide()
    {
        SetVisible(false);
    }

    /// <summary>
    /// Positions the prompt using a screen-space point.
    ///
    /// IMPORTANT:
    /// - screenPoint should already be in screen coordinates
    /// - worldCamera should be null for Screen Space Overlay canvases
    /// - worldCamera should be the canvas camera for Screen Space Camera canvases
    /// </summary>
    public void SetScreenPosition(Vector2 screenPoint, Camera worldCamera)
    {
        if (_rectTransform == null || rootCanvas == null)
        {
            return;
        }

        RectTransform canvasRect = rootCanvas.transform as RectTransform;
        if (canvasRect == null)
        {
            return;
        }

        Camera uiCamera = null;
        if (rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            // For non-overlay canvases, the conversion needs a camera.
            uiCamera = rootCanvas.worldCamera != null ? rootCanvas.worldCamera : worldCamera;
        }

        Vector2 adjustedPoint = screenPoint + screenOffset;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            adjustedPoint,
            uiCamera,
            out Vector2 localPoint))
        {
            _rectTransform.anchoredPosition = localPoint;
        }
    }

    /// <summary>
    /// Returns the canvas this prompt is using.
    /// Useful for other code that needs to know whether it is overlay or camera-space.
    /// </summary>
    public Canvas GetRootCanvas()
    {
        return rootCanvas;
    }

    private void SetVisible(bool isVisible)
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = isVisible ? 1f : 0f;
            _canvasGroup.interactable = isVisible;
            _canvasGroup.blocksRaycasts = false;
            return;
        }

        gameObject.SetActive(isVisible);
    }
}