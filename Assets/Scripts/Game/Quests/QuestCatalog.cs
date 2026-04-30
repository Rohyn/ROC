using UnityEngine;

[CreateAssetMenu(menuName = "ROC/Quests/Quest Catalog")]
public class QuestCatalog : ScriptableObject
{
    [SerializeField] private QuestDefinition[] quests;

    public bool TryGetDefinition(string questId, out QuestDefinition definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(questId) || quests == null)
        {
            return false;
        }

        for (int i = 0; i < quests.Length; i++)
        {
            QuestDefinition candidate = quests[i];

            if (candidate == null)
            {
                continue;
            }

            if (candidate.QuestId == questId)
            {
                definition = candidate;
                return true;
            }
        }

        return false;
    }
}