using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Very small bridge between menu UI buttons and the persistent AppFlowController.
///
/// Responsibilities:
/// - forward Play/Quit button clicks to AppFlowController
/// - disable buttons while connecting
/// - display the current connection status message
///
/// Attach this to a GameObject in the MainMenu scene, such as the main menu panel.
/// </summary>
public class MainMenuView : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The Play button in the main menu.")]
    [SerializeField] private Button playButton;

    [Tooltip("The Quit button in the main menu.")]
    [SerializeField] private Button quitButton;

    [Tooltip("Optional root canvas group. If assigned, the whole menu can be visually dimmed while connecting.")]
    [SerializeField] private CanvasGroup menuCanvasGroup;

    [Tooltip("Status text shown near the buttons. Example: Ready / Connecting... / Connection failed.")]
    [SerializeField] private TMP_Text statusLabel;

    private void OnEnable()
    {
        // If the AppFlowController already exists, subscribe immediately.
        if (AppFlowController.Instance != null)
        {
            AppFlowController.Instance.MenuConnectionStateChanged += HandleMenuConnectionStateChanged;

            // Force an immediate sync so the menu reflects the current stored state
            // even if the event fired before this view finished loading.
            ApplyState(
                AppFlowController.Instance.IsConnecting,
                AppFlowController.Instance.CurrentStatusMessage);
        }
        else
        {
            // Fallback UI state if something is wrong.
            ApplyState(false, "Ready");
        }
    }

    private void OnDisable()
    {
        if (AppFlowController.Instance != null)
        {
            AppFlowController.Instance.MenuConnectionStateChanged -= HandleMenuConnectionStateChanged;
        }
    }

    /// <summary>
    /// Called by the Play button in the MainMenu scene.
    /// </summary>
    public void OnPlayButtonPressed()
    {
        if (AppFlowController.Instance == null)
        {
            Debug.LogError("[MainMenuView] No AppFlowController instance was found.");
            ApplyState(false, "Connection failed: app flow not found.");
            return;
        }

        AppFlowController.Instance.OnPlayPressed();
    }

    /// <summary>
    /// Called by the Quit button in the MainMenu scene.
    /// </summary>
    public void OnQuitButtonPressed()
    {
        if (AppFlowController.Instance == null)
        {
            Debug.LogError("[MainMenuView] No AppFlowController instance was found.");
            return;
        }

        AppFlowController.Instance.OnQuitPressed();
    }

    /// <summary>
    /// Event callback from AppFlowController whenever connection state changes.
    /// </summary>
    private void HandleMenuConnectionStateChanged(bool isConnecting, string statusMessage)
    {
        ApplyState(isConnecting, statusMessage);
    }

    /// <summary>
    /// Applies a visual menu state.
    ///
    /// Current behavior:
    /// - while connecting: disable both buttons
    /// - otherwise: enable both buttons
    /// - always update the status text
    ///
    /// You can later refine this further, for example:
    /// - disable only Play but keep Quit enabled
    /// - hide the button panel on successful connection
    /// - show a spinner while connecting
    /// </summary>
    private void ApplyState(bool isConnecting, string statusMessage)
    {
        // Decide whether buttons should be interactable.
        // Here we disable all buttons while a connection attempt is in progress.
        bool buttonsInteractable = !isConnecting;

        if (playButton != null)
        {
            playButton.interactable = buttonsInteractable;
        }

        if (quitButton != null)
        {
            quitButton.interactable = buttonsInteractable;
        }

        // Optional whole-menu visual dimming.
        if (menuCanvasGroup != null)
        {
            menuCanvasGroup.interactable = buttonsInteractable;
            menuCanvasGroup.blocksRaycasts = buttonsInteractable;

            // Slight dim while connecting, normal otherwise.
            menuCanvasGroup.alpha = isConnecting ? 0.85f : 1f;
        }

        if (statusLabel != null)
        {
            statusLabel.text = string.IsNullOrWhiteSpace(statusMessage)
                ? "Ready"
                : statusMessage;
        }
    }
}