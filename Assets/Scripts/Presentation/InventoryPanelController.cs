using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using ROC.Inventory;

/// <summary>
/// Persistent controller for the simple inventory panel.
///
/// RESPONSIBILITIES:
/// - toggle the inventory panel with Tab
/// - find the local player's PlayerInventory
/// - subscribe to inventory changes
/// - keep the inventory view refreshed
/// - coordinate cursor mode with PlayerLookController
///
/// IMPORTANT:
/// This first pass assumes:
/// - Tab opens/closes inventory directly
/// - opening inventory should enter menu cursor mode
/// - closing inventory should return to gameplay-locked cursor mode
///
/// Later, this can evolve into a general multi-tab menu controller.
/// </summary>
[DisallowMultipleComponent]
public class InventoryPanelController : MonoBehaviour
{
    [Header("Required References")]
    [Tooltip("The persistent inventory panel view.")]
    [SerializeField] private InventoryPanelView panelView;

    [Header("Binding")]
    [Tooltip("How often to retry finding the local player's inventory / look controller when not currently bound.")]
    [SerializeField] private float searchIntervalSeconds = 0.5f;

    [Header("Debug")]
    [Tooltip("If true, panel open/close and binding events will be logged.")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerInventory _boundInventory;
    private PlayerLookController _boundLookController;
    private float _nextSearchTime;
    private bool _isOpen;

    private void Awake()
    {
        if (panelView == null)
        {
            panelView = FindFirstObjectByType<InventoryPanelView>();
        }

        if (panelView != null)
        {
            panelView.Hide();
        }

        _isOpen = false;
    }

    private void OnEnable()
    {
        TryBindDependencies(force: true);
    }

    private void OnDisable()
    {
        UnbindInventory();

        if (panelView != null)
        {
            panelView.Hide();
        }

        _isOpen = false;
    }

    private void Update()
    {
        if (Time.time >= _nextSearchTime)
        {
            TryBindDependencies(force: false);
            _nextSearchTime = Time.time + searchIntervalSeconds;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.tabKey.wasPressedThisFrame)
        {
            TogglePanel();
        }
    }

    private void TogglePanel()
    {
        _isOpen = !_isOpen;

        if (_isOpen)
        {
            OpenPanel();
        }
        else
        {
            ClosePanel();
        }
    }

    private void OpenPanel()
    {
        if (panelView != null)
        {
            panelView.Show();
            panelView.RenderInventory(_boundInventory);
        }

        if (_boundLookController != null)
        {
            _boundLookController.SetCursorMode(PlayerLookController.CursorModeState.MenuCursor);
        }

        if (verboseLogging)
        {
            Debug.Log("[InventoryPanelController] Inventory panel opened.");
        }
    }

    private void ClosePanel()
    {
        if (panelView != null)
        {
            panelView.Hide();
        }

        if (_boundLookController != null)
        {
            _boundLookController.SetCursorMode(PlayerLookController.CursorModeState.GameplayLocked);
        }

        if (verboseLogging)
        {
            Debug.Log("[InventoryPanelController] Inventory panel closed.");
        }
    }

    private void TryBindDependencies(bool force)
    {
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

                _boundLookController = lookController;

                if (verboseLogging)
                {
                    Debug.Log("[InventoryPanelController] Bound local PlayerLookController.");
                }

                break;
            }
        }

        if (_isOpen && panelView != null)
        {
            panelView.RenderInventory(_boundInventory);
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
            Debug.Log("[InventoryPanelController] Bound local PlayerInventory.");
        }

        if (_isOpen && panelView != null)
        {
            panelView.RenderInventory(_boundInventory);
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

    private void HandleInventoryChanged()
    {
        if (_isOpen && panelView != null)
        {
            panelView.RenderInventory(_boundInventory);
        }
    }
}