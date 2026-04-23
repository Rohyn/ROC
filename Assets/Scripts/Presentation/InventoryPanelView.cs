using TMPro;
using UnityEngine;
using ROC.Inventory;

/// <summary>
/// Inventory tab view with two lists:
/// - bag items
/// - equipped items
///
/// This version uses row prefabs instead of a single TMP text block,
/// because right-click equip/unequip requires per-item UI elements.
///
/// CURRENT INTERACTION:
/// - right-click an equippable item in the bag list -> request equip
/// - right-click an item in the equipped list -> request unequip
///
/// Non-equippable bag items (like the infirmary key) ignore right-click.
/// </summary>
[DisallowMultipleComponent]
public class InventoryPanelView : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Optional Labels")]
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private string titleText = "Inventory";

    [Header("List Containers")]
    [SerializeField] private Transform bagListContainer;
    [SerializeField] private Transform equippedListContainer;

    [Header("Row Prefab")]
    [SerializeField] private InventoryItemRowView rowPrefab;

    [Header("Fallback Empty Text")]
    [SerializeField] private TMP_Text bagEmptyLabel;
    [SerializeField] private TMP_Text equippedEmptyLabel;
    [SerializeField] private string emptyBagText = "Bags empty.";
    [SerializeField] private string emptyEquippedText = "Nothing equipped.";

    private PlayerInventory _boundInventory;

    private void Awake()
    {
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        if (titleLabel != null)
        {
            titleLabel.text = titleText;
        }

        Hide();
    }

    public void Show()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
    }

    public void Hide()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    public bool IsVisible()
    {
        return panelRoot != null && panelRoot.activeSelf;
    }

    /// <summary>
    /// Rebuilds both bag and equipped item lists from the bound inventory.
    /// </summary>
    public void RenderInventory(PlayerInventory inventory)
    {
        _boundInventory = inventory;

        ClearContainer(bagListContainer);
        ClearContainer(equippedListContainer);

        if (_boundInventory == null)
        {
            SetEmptyLabels(true, true);
            return;
        }

        BuildBagList();
        BuildEquippedList();
    }

    private void BuildBagList()
    {
        if (bagListContainer == null || rowPrefab == null || _boundInventory == null)
        {
            SetBagEmptyLabel(true);
            return;
        }

        int entryCount = _boundInventory.GetEntryCount(PlayerInventory.InventoryCollection.Bag);
        SetBagEmptyLabel(entryCount <= 0);

        for (int i = 0; i < entryCount; i++)
        {
            if (!_boundInventory.TryGetDisplayInfoAt(
                i,
                PlayerInventory.InventoryCollection.Bag,
                out string itemId,
                out string displayName,
                out int quantity,
                out bool isEquippable))
            {
                continue;
            }

            InventoryItemRowView row = Instantiate(rowPrefab, bagListContainer);
            row.Bind(
                itemId,
                displayName,
                quantity,
                isEquippable,
                InventoryItemRowView.RowCollection.Bag,
                HandleRowRightClick);
        }
    }

    private void BuildEquippedList()
    {
        if (equippedListContainer == null || rowPrefab == null || _boundInventory == null)
        {
            SetEquippedEmptyLabel(true);
            return;
        }

        int entryCount = _boundInventory.GetEntryCount(PlayerInventory.InventoryCollection.Equipped);
        SetEquippedEmptyLabel(entryCount <= 0);

        for (int i = 0; i < entryCount; i++)
        {
            if (!_boundInventory.TryGetDisplayInfoAt(
                i,
                PlayerInventory.InventoryCollection.Equipped,
                out string itemId,
                out string displayName,
                out int quantity,
                out bool isEquippable))
            {
                continue;
            }

            InventoryItemRowView row = Instantiate(rowPrefab, equippedListContainer);
            row.Bind(
                itemId,
                displayName,
                quantity,
                isEquippable,
                InventoryItemRowView.RowCollection.Equipped,
                HandleRowRightClick);
        }
    }

    private void HandleRowRightClick(string itemId, InventoryItemRowView.RowCollection rowCollection, bool isEquippable)
    {
        if (_boundInventory == null || string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        switch (rowCollection)
        {
            case InventoryItemRowView.RowCollection.Bag:
            {
                // Only equippable bag items respond to right-click equip.
                if (!isEquippable)
                {
                    return;
                }

                _boundInventory.RequestEquipItem(itemId);
                break;
            }

            case InventoryItemRowView.RowCollection.Equipped:
            {
                _boundInventory.RequestUnequipItem(itemId);
                break;
            }
        }
    }

    private void ClearContainer(Transform container)
    {
        if (container == null)
        {
            return;
        }

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Destroy(container.GetChild(i).gameObject);
        }
    }

    private void SetEmptyLabels(bool bagEmpty, bool equippedEmpty)
    {
        SetBagEmptyLabel(bagEmpty);
        SetEquippedEmptyLabel(equippedEmpty);
    }

    private void SetBagEmptyLabel(bool isVisible)
    {
        if (bagEmptyLabel != null)
        {
            bagEmptyLabel.gameObject.SetActive(isVisible);
            if (isVisible)
            {
                bagEmptyLabel.text = emptyBagText;
            }
        }
    }

    private void SetEquippedEmptyLabel(bool isVisible)
    {
        if (equippedEmptyLabel != null)
        {
            equippedEmptyLabel.gameObject.SetActive(isVisible);
            if (isVisible)
            {
                equippedEmptyLabel.text = emptyEquippedText;
            }
        }
    }
}