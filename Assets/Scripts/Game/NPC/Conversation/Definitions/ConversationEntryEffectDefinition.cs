using System;
using UnityEngine;

[Serializable]
public class ConversationEntryEffectDefinition
{
    [Header("Quest Effects")]
    [SerializeField] private QuestDefinition questToAccept;

    [Header("Progress Flag Effects")]
    [SerializeField] private string[] progressFlagsToGrant;

    public bool HasAnyEffect
    {
        get
        {
            return questToAccept != null ||
                   (progressFlagsToGrant != null && progressFlagsToGrant.Length > 0);
        }
    }

    public void Apply(GameObject playerObject)
    {
        if (playerObject == null)
        {
            return;
        }

        ApplyQuestEffects(playerObject);
        ApplyProgressFlagEffects(playerObject);
    }

    private void ApplyQuestEffects(GameObject playerObject)
    {
        if (questToAccept == null)
        {
            return;
        }

        PlayerQuestLog questLog = playerObject.GetComponent<PlayerQuestLog>();

        if (questLog == null)
        {
            Debug.LogWarning(
                $"[ConversationEntryEffectDefinition] Cannot accept quest '{questToAccept.QuestId}' because player has no PlayerQuestLog.",
                playerObject);

            return;
        }

        questLog.TryAcceptQuest(questToAccept);
    }

    private void ApplyProgressFlagEffects(GameObject playerObject)
    {
        if (progressFlagsToGrant == null || progressFlagsToGrant.Length == 0)
        {
            return;
        }

        PlayerProgressState progressState = playerObject.GetComponent<PlayerProgressState>();

        if (progressState == null)
        {
            Debug.LogWarning(
                "[ConversationEntryEffectDefinition] Cannot grant progress flags because player has no PlayerProgressState.",
                playerObject);

            return;
        }

        progressState.GrantFlags(progressFlagsToGrant);
    }
}