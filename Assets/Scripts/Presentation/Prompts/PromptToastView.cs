using TMPro;
using UnityEngine;

/// <summary>
/// Pure UI view for prompt / bark / guidance text.
/// 
/// This version formats control tokens in prompt messages.
/// Example authored prompt text:
/// - Press {interact} to use the bed.
/// - Press {menu} to open your menu.
/// - Press {key:inventory.toggle} to open inventory.
/// </summary>
[DisallowMultipleComponent]
public class PromptToastView : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject rootObject;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Text")]
    [SerializeField] private TMP_Text speakerNameText;
    [SerializeField] private TMP_Text messageText;

    [Header("Control Tokens")]
    [Tooltip("If true, message text can contain keybind tokens like {interact}, {menu}, or {key:interact}.")]
    [SerializeField] private bool formatControlTokens = true;

    [Tooltip("If true, prompt keys are displayed like [E] instead of E.")]
    [SerializeField] private bool wrapFormattedKeysInBrackets = true;

    private void Awake()
    {
        if (rootObject == null)
        {
            rootObject = gameObject;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        Hide();
    }

    public void Show(string speakerName, string message)
    {
        if (speakerNameText != null)
        {
            speakerNameText.text = speakerName ?? string.Empty;
        }

        if (messageText != null)
        {
            messageText.text = formatControlTokens
                ? ControlPromptFormatter.Format(message, wrapFormattedKeysInBrackets)
                : message ?? string.Empty;
        }

        SetVisible(true);

        if (rootObject != null)
        {
            rootObject.transform.SetAsLastSibling();
        }
    }

    public void Hide()
    {
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (rootObject != null)
        {
            rootObject.SetActive(visible);
        }
    }
}
