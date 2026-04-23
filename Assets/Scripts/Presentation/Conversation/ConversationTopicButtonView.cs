using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple topic button view used in the conversation panel.
/// </summary>
[DisallowMultipleComponent]
public class ConversationTopicButtonView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    private string _topicId;
    private Action<string> _onClicked;

    private void Awake()
    {
        if (button != null)
        {
            button.onClick.AddListener(HandleClicked);
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClicked);
        }
    }

    public void Bind(string topicId, string displayLabel, Action<string> onClicked)
    {
        _topicId = topicId;
        _onClicked = onClicked;

        if (label != null)
        {
            label.text = displayLabel;
        }
    }

    private void HandleClicked()
    {
        if (!string.IsNullOrWhiteSpace(_topicId))
        {
            _onClicked?.Invoke(_topicId);
        }
    }
}