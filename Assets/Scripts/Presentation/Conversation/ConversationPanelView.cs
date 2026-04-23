using TMPro;
using UnityEngine;

/// <summary>
/// View for the persistent conversation panel.
///
/// This class owns references to the conversation UI elements,
/// but does not contain session logic.
/// </summary>
[DisallowMultipleComponent]
public class ConversationPanelView : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

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
}