using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple side-tab controller for the account settings panel.
/// 
/// Use this when the AccountSettingsPanel has:
/// - a left column of buttons
/// - a stack of category panels to the right
/// 
/// It only changes visible UI panels. Account settings data is still handled by
/// AccountSettingsPanelView and GameSettingsService.
/// </summary>
[DisallowMultipleComponent]
public sealed class AccountSettingsCategoryTabsController : MonoBehaviour
{
    [Serializable]
    public class SettingsCategoryTab
    {
        [Tooltip("Stable category ID. Example: audio, camera, graphics, keybinds")]
        public string categoryId;

        [Tooltip("Button in the left-side category column.")]
        public Button button;

        [Tooltip("Panel shown to the right when this category is selected.")]
        public GameObject panelRoot;

        [Tooltip("Optional visual marker enabled when this category is selected.")]
        public GameObject selectedIndicator;

        [Tooltip("Optional label to style when selected/unselected.")]
        public TMP_Text label;
    }

    [Header("Tabs")]
    [SerializeField] private SettingsCategoryTab[] tabs;

    [Header("Startup")]
    [SerializeField] private string defaultCategoryId = "audio";
    [SerializeField] private bool selectDefaultOnEnable = true;

    [Header("Button Behavior")]
    [Tooltip("If true, the selected category button is made non-interactable.")]
    [SerializeField] private bool disableSelectedButton = true;

    [Header("Optional Label Styling")]
    [SerializeField] private bool styleLabels = false;
    [SerializeField] private Color selectedLabelColor = Color.white;
    [SerializeField] private Color unselectedLabelColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    public string CurrentCategoryId { get; private set; } = string.Empty;

    private void Awake()
    {
        HideAllPanels();
    }

    private void OnEnable()
    {
        HookButtons();

        if (selectDefaultOnEnable || string.IsNullOrWhiteSpace(CurrentCategoryId))
        {
            SelectDefaultOrFirstValidTab();
        }
        else
        {
            SelectCategory(CurrentCategoryId);
        }
    }

    private void OnDisable()
    {
        UnhookButtons();
    }

    public void SelectCategory(string categoryId)
    {
        string normalizedCategoryId = NormalizeId(categoryId);

        if (string.IsNullOrWhiteSpace(normalizedCategoryId))
        {
            SelectDefaultOrFirstValidTab();
            return;
        }

        int index = FindTabIndex(normalizedCategoryId);

        if (index < 0)
        {
            if (verboseLogging)
            {
                Debug.LogWarning(
                    $"[AccountSettingsCategoryTabsController] No settings tab found for category '{normalizedCategoryId}'.",
                    this);
            }

            SelectDefaultOrFirstValidTab();
            return;
        }

        SelectIndex(index);
    }

    public void SelectIndex(int index)
    {
        if (tabs == null || index < 0 || index >= tabs.Length)
        {
            return;
        }

        SettingsCategoryTab selectedTab = tabs[index];

        if (selectedTab == null)
        {
            return;
        }

        CurrentCategoryId = NormalizeId(selectedTab.categoryId);

        for (int i = 0; i < tabs.Length; i++)
        {
            SettingsCategoryTab tab = tabs[i];

            if (tab == null)
            {
                continue;
            }

            bool selected = i == index;

            if (tab.panelRoot != null)
            {
                tab.panelRoot.SetActive(selected);
            }

            if (tab.selectedIndicator != null)
            {
                tab.selectedIndicator.SetActive(selected);
            }

            if (tab.button != null && disableSelectedButton)
            {
                tab.button.interactable = !selected;
            }

            if (styleLabels && tab.label != null)
            {
                tab.label.color = selected ? selectedLabelColor : unselectedLabelColor;
            }
        }

        if (verboseLogging)
        {
            Debug.Log($"[AccountSettingsCategoryTabsController] Selected account settings category '{CurrentCategoryId}'.", this);
        }
    }

    public void SelectAudio()
    {
        SelectCategory("audio");
    }

    public void SelectCamera()
    {
        SelectCategory("camera");
    }

    public void SelectGraphics()
    {
        SelectCategory("graphics");
    }

    public void SelectKeybinds()
    {
        SelectCategory("keybinds");
    }

    private void HookButtons()
    {
        if (tabs == null)
        {
            return;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            SettingsCategoryTab tab = tabs[i];

            if (tab == null || tab.button == null)
            {
                continue;
            }

            int capturedIndex = i;
            tab.button.onClick.RemoveAllListeners();
            tab.button.onClick.AddListener(() => SelectIndex(capturedIndex));
        }
    }

    private void UnhookButtons()
    {
        if (tabs == null)
        {
            return;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            SettingsCategoryTab tab = tabs[i];

            if (tab == null || tab.button == null)
            {
                continue;
            }

            tab.button.onClick.RemoveAllListeners();
        }
    }

    private void SelectDefaultOrFirstValidTab()
    {
        int defaultIndex = FindTabIndex(defaultCategoryId);

        if (defaultIndex >= 0)
        {
            SelectIndex(defaultIndex);
            return;
        }

        if (tabs == null)
        {
            return;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            SettingsCategoryTab tab = tabs[i];

            if (tab == null)
            {
                continue;
            }

            if (tab.button == null && tab.panelRoot == null)
            {
                continue;
            }

            SelectIndex(i);
            return;
        }
    }

    private int FindTabIndex(string categoryId)
    {
        string normalizedCategoryId = NormalizeId(categoryId);

        if (tabs == null || string.IsNullOrWhiteSpace(normalizedCategoryId))
        {
            return -1;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            SettingsCategoryTab tab = tabs[i];

            if (tab == null)
            {
                continue;
            }

            if (NormalizeId(tab.categoryId) == normalizedCategoryId)
            {
                return i;
            }
        }

        return -1;
    }

    private void HideAllPanels()
    {
        if (tabs == null)
        {
            return;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            SettingsCategoryTab tab = tabs[i];

            if (tab == null)
            {
                continue;
            }

            if (tab.panelRoot != null)
            {
                tab.panelRoot.SetActive(false);
            }

            if (tab.selectedIndicator != null)
            {
                tab.selectedIndicator.SetActive(false);
            }

            if (tab.button != null)
            {
                tab.button.interactable = true;
            }

            if (styleLabels && tab.label != null)
            {
                tab.label.color = unselectedLabelColor;
            }
        }
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }
}
