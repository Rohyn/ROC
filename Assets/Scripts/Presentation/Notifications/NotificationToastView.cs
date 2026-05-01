using TMPro;
using UnityEngine;

/// <summary>
/// Pure UI view for one notification display.
/// 
/// Keep this GameObject active. The view hides itself with CanvasGroup alpha,
/// so other controllers can still find it at runtime.
/// </summary>
[DisallowMultipleComponent]
public class NotificationToastView : MonoBehaviour
{
    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Text")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        Hide();
    }

    public void Show(string title, string body)
    {
        if (titleText != null)
        {
            titleText.text = title ?? string.Empty;
            titleText.gameObject.SetActive(!string.IsNullOrWhiteSpace(title));
        }

        if (bodyText != null)
        {
            bodyText.text = body ?? string.Empty;
            bodyText.gameObject.SetActive(!string.IsNullOrWhiteSpace(body));
        }

        SetVisible(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
