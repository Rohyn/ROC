using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Simple main-menu settings panel controller.
/// 
/// Use this on the main menu canvas/root. It opens an account-level settings panel
/// from a Settings button and closes it from Back/Close/Escape.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuSettingsPanelController : MonoBehaviour
{
    [Header("Roots")]
    [Tooltip("Optional. Assign the normal main menu buttons/root here if you want them hidden while settings are open.")]
    [SerializeField] private GameObject mainMenuRootToHide;

    [Tooltip("The root object of the account settings panel.")]
    [SerializeField] private GameObject settingsPanelRoot;

    [Header("Buttons")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button backButton;

    [Header("Behavior")]
    [SerializeField] private bool hideMainMenuRootWhileSettingsOpen = true;
    [SerializeField] private bool closeWithCancelKey = true;
    [SerializeField] private Key fallbackCancelKey = Key.Escape;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        GameSettingsService.GetOrCreate();
        CloseSettings();
    }

    private void OnEnable()
    {
        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(OpenSettings);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseSettings);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(CloseSettings);
        }
    }

    private void OnDisable()
    {
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OpenSettings);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseSettings);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(CloseSettings);
        }
    }

    private void Update()
    {
        if (!IsOpen || !closeWithCancelKey)
        {
            return;
        }

        if (RocInput.WasPressedThisFrame(KeybindActionId.Cancel, fallbackCancelKey))
        {
            CloseSettings();
        }
    }

    public void OpenSettings()
    {
        IsOpen = true;

        GameSettingsService.GetOrCreate();

        if (settingsPanelRoot != null)
        {
            settingsPanelRoot.SetActive(true);
        }

        if (mainMenuRootToHide != null && hideMainMenuRootWhileSettingsOpen)
        {
            mainMenuRootToHide.SetActive(false);
        }

        if (verboseLogging)
        {
            Debug.Log("[MainMenuSettingsPanelController] Opened account settings panel.", this);
        }
    }

    public void CloseSettings()
    {
        IsOpen = false;

        if (settingsPanelRoot != null)
        {
            settingsPanelRoot.SetActive(false);
        }

        if (mainMenuRootToHide != null && hideMainMenuRootWhileSettingsOpen)
        {
            mainMenuRootToHide.SetActive(true);
        }

        if (verboseLogging)
        {
            Debug.Log("[MainMenuSettingsPanelController] Closed account settings panel.", this);
        }
    }
}
