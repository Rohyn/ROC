using System.Text;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class JournalPanelView : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Labels")]
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text activeQuestsLabel;
    [SerializeField] private TMP_Text completedQuestsLabel;

    [Header("Text")]
    [SerializeField] private string titleText = "Journal";
    [SerializeField] private string noActiveQuestsText = "No active quests.";
    [SerializeField] private string noCompletedQuestsText = "No completed quests.";

    private readonly StringBuilder _builder = new();

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

    public void RenderJournal(PlayerQuestLog questLog)
    {
        RenderActiveQuests(questLog);
        RenderCompletedQuests(questLog);
    }

    private void RenderActiveQuests(PlayerQuestLog questLog)
    {
        if (activeQuestsLabel == null)
        {
            return;
        }

        if (questLog == null || questLog.JournalActiveQuests == null || questLog.JournalActiveQuests.Count == 0)
        {
            activeQuestsLabel.text = noActiveQuestsText;
            return;
        }

        _builder.Clear();
        _builder.AppendLine("Active Quests");
        _builder.AppendLine();

        for (int i = 0; i < questLog.JournalActiveQuests.Count; i++)
        {
            QuestJournalEntryData quest = questLog.JournalActiveQuests[i];

            if (quest == null)
            {
                continue;
            }

            _builder.AppendLine(quest.Title);

            if (!string.IsNullOrWhiteSpace(quest.Description))
            {
                _builder.AppendLine(quest.Description);
            }

            if (!string.IsNullOrWhiteSpace(quest.ObjectiveText))
            {
                _builder.Append("- ");
                _builder.Append(quest.ObjectiveText);
                _builder.Append(" ");
                _builder.Append(quest.Current);
                _builder.Append("/");
                _builder.AppendLine(quest.Required.ToString());
            }

            if (quest.ReadyToTurnIn)
            {
                _builder.AppendLine("Ready to turn in.");
            }

            if (i < questLog.JournalActiveQuests.Count - 1)
            {
                _builder.AppendLine();
            }
        }

        activeQuestsLabel.text = _builder.ToString();
    }

    private void RenderCompletedQuests(PlayerQuestLog questLog)
    {
        if (completedQuestsLabel == null)
        {
            return;
        }

        if (questLog == null || questLog.JournalCompletedQuestIds == null || questLog.JournalCompletedQuestIds.Count == 0)
        {
            completedQuestsLabel.text = noCompletedQuestsText;
            return;
        }

        _builder.Clear();
        _builder.AppendLine("Completed");
        _builder.AppendLine();

        for (int i = 0; i < questLog.JournalCompletedQuestIds.Count; i++)
        {
            _builder.AppendLine(questLog.JournalCompletedQuestIds[i]);
        }

        completedQuestsLabel.text = _builder.ToString();
    }
}