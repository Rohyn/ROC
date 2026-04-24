using UnityEngine;

/// <summary>
/// Small helper for sending gameplay events into the local player's quest log.
///
/// This keeps quest event emission from being repeated everywhere.
/// Systems such as interaction, inventory, equipment, and conversation can all
/// use this helper when they want to report normalized quest-relevant events.
/// </summary>
public static class QuestEventUtility
{
    public static void EmitToPlayer(GameObject playerObject, GameplayEventData eventData)
    {
        if (playerObject == null || eventData == null)
        {
            return;
        }

        PlayerQuestLog questLog = playerObject.GetComponent<PlayerQuestLog>();
        if (questLog == null)
        {
            return;
        }

        questLog.RecordGameplayEvent(eventData);
    }
}