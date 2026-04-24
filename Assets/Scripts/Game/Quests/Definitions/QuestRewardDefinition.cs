using System;
using ROC.Inventory;
using UnityEngine;

/// <summary>
/// Reward data for a quest.
///
/// This first version supports:
/// - item rewards
/// - progress flags granted on completion
/// - money value as data only
///
/// IMPORTANT:
/// Money is included as data now even though there may not yet be a currency system.
/// That lets authored quest data be future-compatible.
/// </summary>
[Serializable]
public class QuestRewardDefinition
{
    [Header("Currency")]
    [SerializeField] private int moneyReward = 0;

    [Header("Item Rewards")]
    [SerializeField] private QuestItemRewardEntry[] itemRewards;

    [Header("Progress Flags")]
    [SerializeField] private string[] progressFlagsToGrant;

    public int MoneyReward => moneyReward;
    public QuestItemRewardEntry[] ItemRewards => itemRewards;
    public string[] ProgressFlagsToGrant => progressFlagsToGrant;

    /// <summary>
    /// Applies currently-supported rewards to the player.
    ///
    /// Current support:
    /// - items via PlayerInventory
    /// - progress flags via PlayerProgressState
    ///
    /// Not yet supported:
    /// - money payout logic
    /// </summary>
    public void ApplyTo(GameObject playerObject)
    {
        if (playerObject == null)
        {
            return;
        }

        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>();
        PlayerProgressState progressState = playerObject.GetComponent<PlayerProgressState>();

        if (inventory != null && itemRewards != null)
        {
            for (int i = 0; i < itemRewards.Length; i++)
            {
                QuestItemRewardEntry reward = itemRewards[i];
                if (reward == null || reward.Item == null || reward.Quantity <= 0)
                {
                    continue;
                }

                inventory.AddItem(reward.Item, reward.Quantity);
            }
        }

        if (progressState != null && progressFlagsToGrant != null)
        {
            progressState.GrantFlags(progressFlagsToGrant);
        }
    }
}

/// <summary>
/// One item reward entry.
/// </summary>
[Serializable]
public class QuestItemRewardEntry
{
    [SerializeField] private ItemDefinition item;
    [Min(1)]
    [SerializeField] private int quantity = 1;

    public ItemDefinition Item => item;
    public int Quantity => Mathf.Max(1, quantity);
}