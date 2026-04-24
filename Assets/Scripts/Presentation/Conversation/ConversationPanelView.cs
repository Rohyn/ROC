using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View for the persistent conversation panel.
///
/// This class owns references to the conversation UI elements,
/// but does not contain session logic.
///
/// This version explicitly forces UI layout rebuilds when shown,
/// which helps with first-open issues on initially inactive UI trees.
/// </summary>
[DisallowMultipleComponent]
public class ConversationPanelView : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Optional nested content root under the panel, for example a child named 'Panel'.")]
    [SerializeField] private GameObject contentRoot;

    [Header("Text")]
    [SerializeField] private TMP_Text speakerNameText;
    [SerializeField] private TMP_Text responseText;

    [Header("Topics")]
    [SerializeField] private Transform topicButtonContainer;
    [SerializeField] private ConversationTopicButtonView topicButtonPrefab;

    [Header("Close")]
    [SerializeField] private GameObject closeButtonObject;

    public Transform TopicButtonContainer => topicButtonContainer;
    public ConversationTopicButtonView TopicButtonPrefab => topicButtonPrefab;

    private void Awake()
    {
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }
    }

    public void Show()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();
        }

        if (contentRoot != null)
        {
            contentRoot.SetActive(true);
        }

        if (closeButtonObject != null)
        {
            closeButtonObject.SetActive(true);
        }

        ForceImmediateLayoutRebuild();
    }

    public void Hide()
    {
        if (contentRoot != null)
        {
            contentRoot.SetActive(false);
        }

        if (closeButtonObject != null)
        {
            closeButtonObject.SetActive(false);
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    public bool IsVisible()
    {
        return panelRoot != null && panelRoot.activeSelf;
    }

    public void SetSpeakerName(string speakerName)
    {
        if (speakerNameText != null)
        {
            speakerNameText.text = speakerName;
        }
    }

    public void SetResponseText(string text)
    {
        if (responseText != null)
        {
            responseText.text = text;
        }
    }

    /// <summary>
    /// Forces Unity UI to rebuild this panel immediately.
    /// Useful when showing a UI subtree that started inactive.
    /// </summary>
    public void ForceImmediateLayoutRebuild()
    {
        Canvas.ForceUpdateCanvases();

        if (panelRoot != null)
        {
            RectTransform rootRect = panelRoot.transform as RectTransform;
            if (rootRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            }
        }

        if (contentRoot != null)
        {
            RectTransform contentRect = contentRoot.transform as RectTransform;
            if (contentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            }
        }

        if (topicButtonContainer != null)
        {
            RectTransform topicRect = topicButtonContainer as RectTransform;
            if (topicRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(topicRect);
            }
        }

        Canvas.ForceUpdateCanvases();
    }
}