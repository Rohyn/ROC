using TMPro;
using UnityEngine;

/// <summary>
/// Pure UI view for prompt / bark / guidance text.
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
            messageText.text = message ?? string.Empty;
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
