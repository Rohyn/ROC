using System.Text;
using TMPro;
using UnityEngine;
using ROC.Inventory;

/// <summary>
/// Very simple persistent inventory panel view.
///
/// RESPONSIBILITIES:
/// - show / hide the panel
/// - render the current inventory contents into a TMP text field
///
/// This first pass intentionally uses a single TMP text block rather than a dynamic slot grid.
/// That keeps the UI simple while you establish the menu flow.
/// </summary>
[DisallowMultipleComponent]
public class InventoryPanelView : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Root object for the inventory panel. If left empty, this GameObject is used.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Optional title label.")]
    [SerializeField] private TMP_Text titleLabel;

    [Tooltip("TMP text used to list the inventory contents.")]
    [SerializeField] private TMP_Text contentsLabel;

    [Header("Display")]
    [Tooltip("Title shown at the top of the panel.")]
    [SerializeField] private string titleText = "Inventory";

    [Tooltip("Text shown when the inventory is empty.")]
    [SerializeField] private string emptyInventoryText = "Inventory is empty.";

    [Tooltip("Text shown when the local player inventory has not been found yet.")]
    [SerializeField] private string unavailableText = "Inventory unavailable.";

    private readonly StringBuilder _builder = new StringBuilder(256);

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
    /// Renders the inventory contents into the TMP text field.
    /// </summary>
    public void RenderInventory(PlayerInventory inventory)
    {
        if (contentsLabel == null)
        {
            return;
        }

        if (inventory == null)
        {
            contentsLabel.text = unavailableText;
            return;
        }

        int entryCount = inventory.GetEntryCount();
        if (entryCount <= 0)
        {
            contentsLabel.text = emptyInventoryText;
            return;
        }

        _builder.Clear();

        for (int i = 0; i < entryCount; i++)
        {
            if (!inventory.TryGetDisplayInfoAt(i, out string itemId, out string displayName, out int quantity))
            {
                continue;
            }

            _builder.Append(displayName);

            if (quantity > 1)
            {
                _builder.Append(" x");
                _builder.Append(quantity);
            }

            if (i < entryCount - 1)
            {
                _builder.AppendLine();
            }
        }

        contentsLabel.text = _builder.Length > 0
            ? _builder.ToString()
            : emptyInventoryText;
    }
}