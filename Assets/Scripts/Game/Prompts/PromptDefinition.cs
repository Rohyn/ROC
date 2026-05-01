using UnityEngine;

/// <summary>
/// Data asset for one reusable prompt / bark / guidance line.
/// Not tutorial-specific: this can be used for scenarios, combat barks, quest steps,
/// rituals, crafting guidance, or intro/tutorial prompts.
/// </summary>
[CreateAssetMenu(menuName = "ROC/Prompts/Prompt Definition")]
public class PromptDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string promptId = "prompt.new";
    [SerializeField] private PromptChannel channel = PromptChannel.General;

    [Header("Speaker / Text")]
    [SerializeField] private string speakerName = "Aidan";

    [TextArea(2, 6)]
    [SerializeField] private string messageText = "Prompt text.";

    [Header("Queue / Display")]
    [SerializeField] private int priority = 300;
    [SerializeField] private float displaySeconds = 4f;
    [SerializeField] private float cooldownSeconds = 0f;
    [SerializeField] private PromptRepeatMode repeatMode = PromptRepeatMode.OncePerSession;

    [Header("Optional State Conditions")]
    [SerializeField] private PromptConditionSet conditions = new PromptConditionSet();

    public string PromptId => promptId;
    public PromptChannel Channel => channel;
    public string SpeakerName => speakerName;
    public string MessageText => messageText;
    public int Priority => priority;
    public float DisplaySeconds => Mathf.Max(0.25f, displaySeconds);
    public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
    public PromptRepeatMode RepeatMode => repeatMode;
    public PromptConditionSet Conditions => conditions;

    public bool IsEligibleByState(
        PlayerProgressState progressState,
        ROC.Inventory.PlayerInventory inventory,
        PlayerQuestLog questLog,
        PlayerConversationState conversationState)
    {
        return conditions == null ||
               conditions.IsSatisfiedBy(progressState, inventory, questLog, conversationState);
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(promptId))
        {
            promptId = name;
        }

        if (displaySeconds < 0.25f)
        {
            displaySeconds = 0.25f;
        }

        if (cooldownSeconds < 0f)
        {
            cooldownSeconds = 0f;
        }
    }
}
