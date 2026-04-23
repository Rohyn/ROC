using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// UI row for one inventory or equipped item entry.
///
/// This row supports:
/// - display name
/// - quantity
/// - right-click callback
///
/// Attach this to a row prefab under your inventory UI.
/// </summary>
[DisallowMultipleComponent]
public class InventoryItemRowView : MonoBehaviour, IPointerClickHandler
{
    public enum RowCollection
    {
        Bag = 0,
        Equipped = 1
    }

    [Header("UI References")]
    [SerializeField] private TMP_Text itemNameLabel;
    [SerializeField] private TMP_Text quantityLabel;

    private string _itemId;
    private RowCollection _rowCollection;
    private bool _isEquippable;
    private Action<string, RowCollection, bool> _onRightClick;

    /// <summary>
    /// Initializes this row.
    /// </summary>
    public void Bind(
        string itemId,
        string displayName,
        int quantity,
        bool isEquippable,
        RowCollection rowCollection,
        Action<string, RowCollection, bool> onRightClick)
    {
        _itemId = itemId;
        _rowCollection = rowCollection;
        _isEquippable = isEquippable;
        _onRightClick = onRightClick;

        if (itemNameLabel != null)
        {
            itemNameLabel.text = displayName;
        }

        if (quantityLabel != null)
        {
            quantityLabel.text = quantity > 1 ? $"x{quantity}" : string.Empty;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null)
        {
            return;
        }

        Debug.Log($"[InventoryItemRowView] Clicked row for {_itemId} with button {eventData.button}");

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            _onRightClick?.Invoke(_itemId, _rowCollection, _isEquippable);
        }
    }
}