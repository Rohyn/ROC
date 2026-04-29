using ROC.Inventory;
using UnityEngine;

/// <summary>
/// Persistent menu controller.
///
/// DESIGN:
/// - The PlayerLookController owns cursor/input mode.
/// - This controller listens to cursor mode changes.
/// - When cursor mode enters MenuCursor, the Menu panel opens.
/// - When cursor mode leaves MenuCursor, the Menu panel closes.
/// - The menu remembers the last selected tab for this session.
/// </summary>
[DisallowMultipleComponent]
public class MenuController : MonoBehaviour
{
    public enum MenuTab
    {
        Inventory = 0,
        Journal = 1
    }

    [Header("Menu Root")]
    [Tooltip("The root GameObject of the persistent Menu panel.")]
    [SerializeField] private GameObject menuRoot;

    [Header("Tab Views")]
    [SerializeField] private InventoryPanelView inventoryPanelView;
    [SerializeField] private JournalPanelView journalPanelView;

    [Header("Defaults")]
    [SerializeField] private MenuTab defaultTab = MenuTab.Inventory;

    [Header("Binding")]
    [SerializeField] private float searchIntervalSeconds = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerLookController _boundLookController;
    private PlayerInventory _boundInventory;
    private PlayerQuestLog _boundQuestLog;

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
        UnbindQuestLog();

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

    public void SelectInventoryTab()
    {
        ShowTab(MenuTab.Inventory);
    }

    public void SelectJournalTab()
    {
        ShowTab(MenuTab.Journal);
    }

    private void TryBindDependencies(bool force)
    {
        if (force || _boundLookController == null)
        {
            PlayerLookController[] lookControllers = FindObjectsByType<PlayerLookController>(FindObjectsSortMode.None);

            for (int i = 0; i < lookControllers.Length; i++)
            {
                PlayerLookController lookController = lookControllers[i];

                if (lookController == null || !lookController.IsOwner)
                {
                    continue;
                }

                BindLookController(lookController);
                break;
            }
        }

        if (force || _boundInventory == null)
        {
            PlayerInventory[] inventories = FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);

            for (int i = 0; i < inventories.Length; i++)
            {
                PlayerInventory inventory = inventories[i];

                if (inventory == null || !inventory.IsOwner)
                {
                    continue;
                }

                BindInventory(inventory);
                break;
            }
        }

        if (force || _boundQuestLog == null)
        {
            PlayerQuestLog[] questLogs = FindObjectsByType<PlayerQuestLog>(FindObjectsSortMode.None);

            for (int i = 0; i < questLogs.Length; i++)
            {
                PlayerQuestLog questLog = questLogs[i];

                if (questLog == null || !questLog.IsOwner)
                {
                    continue;
                }

                BindQuestLog(questLog);
                break;
            }
        }

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

    private void BindQuestLog(PlayerQuestLog questLog)
    {
        if (_boundQuestLog == questLog)
        {
            return;
        }

        UnbindQuestLog();

        _boundQuestLog = questLog;
        _boundQuestLog.QuestLogChanged += HandleQuestLogChanged;
        _boundQuestLog.RequestQuestJournalSnapshot();

        if (verboseLogging)
        {
            Debug.Log("[MenuController] Bound local PlayerQuestLog.");
        }

        if (_isMenuOpen && _lastSelectedTab == MenuTab.Journal)
        {
            RefreshJournalTab();
        }
    }

    private void UnbindQuestLog()
    {
        if (_boundQuestLog != null)
        {
            _boundQuestLog.QuestLogChanged -= HandleQuestLogChanged;
            _boundQuestLog = null;
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
                RefreshInventoryTab();
                break;
            }

            case MenuTab.Journal:
            {
                RefreshJournalTab();
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

        if (journalPanelView != null)
        {
            journalPanelView.Hide();
        }
    }

    private void HandleInventoryChanged()
    {
        if (!_isMenuOpen || _lastSelectedTab != MenuTab.Inventory)
        {
            return;
        }

        RefreshInventoryTab();
    }

    private void HandleQuestLogChanged()
    {
        if (!_isMenuOpen || _lastSelectedTab != MenuTab.Journal)
        {
            return;
        }

        RefreshJournalTab();
    }

    private void RefreshInventoryTab()
    {
        if (inventoryPanelView == null)
        {
            return;
        }

        inventoryPanelView.Show();
        inventoryPanelView.RenderInventory(_boundInventory);
    }

    private void RefreshJournalTab()
    {
        if (journalPanelView == null)
        {
            return;
        }

        if (_boundQuestLog != null)
        {
            _boundQuestLog.RequestQuestJournalSnapshot();
        }

        journalPanelView.Show();
        journalPanelView.RenderJournal(_boundQuestLog);
    }
}