using UnityEngine;
using ROC.Inventory;

/// <summary>
/// Persistent menu controller.
///
/// DESIGN:
/// - The PlayerLookController owns cursor/input mode.
/// - This controller listens to cursor mode changes.
/// - When cursor mode enters MenuCursor, the Menu panel opens.
/// - When cursor mode leaves MenuCursor, the Menu panel closes.
/// - The menu remembers the last selected tab for this session.
///
/// CURRENT SCOPE:
/// - Only the Inventory tab exists
///
/// FUTURE SCOPE:
/// - Map
/// - Journal
/// - other menu tabs
/// </summary>
[DisallowMultipleComponent]
public class MenuController : MonoBehaviour
{
    public enum MenuTab
    {
        Inventory = 0
    }

    [Header("Menu Root")]
    [Tooltip("The root GameObject of the persistent Menu panel.")]
    [SerializeField] private GameObject menuRoot;

    [Header("Tab Views")]
    [Tooltip("Inventory tab view.")]
    [SerializeField] private InventoryPanelView inventoryPanelView;

    [Header("Defaults")]
    [Tooltip("Which tab should open first if the player has not selected one yet this session.")]
    [SerializeField] private MenuTab defaultTab = MenuTab.Inventory;

    [Header("Binding")]
    [Tooltip("How often to retry finding the local player's look controller and inventory when not yet bound.")]
    [SerializeField] private float searchIntervalSeconds = 0.5f;

    [Header("Debug")]
    [Tooltip("If true, menu open/close and binding events will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerLookController _boundLookController;
    private PlayerInventory _boundInventory;
    private float _nextSearchTime;

    private bool _isMenuOpen;
    private MenuTab _lastSelectedTab;

    private void Awake()
    {
        _lastSelectedTab = defaultTab;

        if (menuRoot != null)
        {
            menuRoot.SetActive(false);
        }

        HideAllTabs();
        _isMenuOpen = false;
    }

    private void OnEnable()
    {
        TryBindDependencies(force: true);
    }

    private void OnDisable()
    {
        UnbindLookController();
        UnbindInventory();

        if (menuRoot != null)
        {
            menuRoot.SetActive(false);
        }

        HideAllTabs();
        _isMenuOpen = false;
    }

    private void Update()
    {
        if (Time.time >= _nextSearchTime)
        {
            TryBindDependencies(force: false);
            _nextSearchTime = Time.time + searchIntervalSeconds;
        }
    }

    /// <summary>
    /// Public hook for future ribbon buttons.
    /// Example: wire a UI button to call SelectInventoryTab().
    /// </summary>
    public void SelectInventoryTab()
    {
        ShowTab(MenuTab.Inventory);
    }

    private void TryBindDependencies(bool force)
    {
        if (force || _boundLookController == null)
        {
            PlayerLookController[] lookControllers =
                FindObjectsByType<PlayerLookController>(FindObjectsSortMode.None);

            for (int i = 0; i < lookControllers.Length; i++)
            {
                PlayerLookController lookController = lookControllers[i];
                if (lookController == null)
                {
                    continue;
                }

                if (!lookController.IsOwner)
                {
                    continue;
                }

                BindLookController(lookController);
                break;
            }
        }

        if (force || _boundInventory == null)
        {
            PlayerInventory[] inventories =
                FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);

            for (int i = 0; i < inventories.Length; i++)
            {
                PlayerInventory inventory = inventories[i];
                if (inventory == null)
                {
                    continue;
                }

                if (!inventory.IsOwner)
                {
                    continue;
                }

                BindInventory(inventory);
                break;
            }
        }

        // If the menu is already open and we newly found inventory, refresh the current tab.
        if (_isMenuOpen)
        {
            ShowTab(_lastSelectedTab);
        }
    }

    private void BindLookController(PlayerLookController lookController)
    {
        if (_boundLookController == lookController)
        {
            return;
        }

        UnbindLookController();

        _boundLookController = lookController;
        _boundLookController.CursorModeChanged += HandleCursorModeChanged;

        if (verboseLogging)
        {
            Debug.Log("[MenuController] Bound local PlayerLookController.");
        }

        // Sync immediately to current mode.
        HandleCursorModeChanged(_boundLookController.CurrentCursorMode);
    }

    private void UnbindLookController()
    {
        if (_boundLookController != null)
        {
            _boundLookController.CursorModeChanged -= HandleCursorModeChanged;
            _boundLookController = null;
        }
    }

    private void BindInventory(PlayerInventory inventory)
    {
        if (_boundInventory == inventory)
        {
            return;
        }

        UnbindInventory();

        _boundInventory = inventory;
        _boundInventory.InventoryChanged += HandleInventoryChanged;

        if (verboseLogging)
        {
            Debug.Log("[MenuController] Bound local PlayerInventory.");
        }

        if (_isMenuOpen && _lastSelectedTab == MenuTab.Inventory)
        {
            RefreshInventoryTab();
        }
    }

    private void UnbindInventory()
    {
        if (_boundInventory != null)
        {
            _boundInventory.InventoryChanged -= HandleInventoryChanged;
            _boundInventory = null;
        }
    }

    private void HandleCursorModeChanged(PlayerLookController.CursorModeState newMode)
    {
        if (newMode == PlayerLookController.CursorModeState.MenuCursor)
        {
            OpenMenu();
        }
        else
        {
            CloseMenu();
        }
    }

    private void OpenMenu()
    {
        if (_isMenuOpen)
        {
            return;
        }

        _isMenuOpen = true;

        if (menuRoot != null)
        {
            menuRoot.SetActive(true);
        }

        ShowTab(_lastSelectedTab);

        if (verboseLogging)
        {
            Debug.Log("[MenuController] Menu opened.");
        }
    }

    private void CloseMenu()
    {
        if (!_isMenuOpen)
        {
            return;
        }

        _isMenuOpen = false;

        HideAllTabs();

        if (menuRoot != null)
        {
            menuRoot.SetActive(false);
        }

        if (verboseLogging)
        {
            Debug.Log("[MenuController] Menu closed.");
        }
    }

    private void ShowTab(MenuTab tab)
    {
        _lastSelectedTab = tab;

        HideAllTabs();

        switch (_lastSelectedTab)
        {
            case MenuTab.Inventory:
            {
                if (inventoryPanelView != null)
                {
                    inventoryPanelView.Show();
                    inventoryPanelView.RenderInventory(_boundInventory);
                }

                break;
            }
        }
    }

    private void HideAllTabs()
    {
        if (inventoryPanelView != null)
        {
            inventoryPanelView.Hide();
        }
    }

    private void HandleInventoryChanged()
    {
        if (!_isMenuOpen)
        {
            return;
        }

        if (_lastSelectedTab == MenuTab.Inventory)
        {
            RefreshInventoryTab();
        }
    }

    private void RefreshInventoryTab()
    {
        if (inventoryPanelView != null)
        {
            inventoryPanelView.RenderInventory(_boundInventory);
        }
    }
}