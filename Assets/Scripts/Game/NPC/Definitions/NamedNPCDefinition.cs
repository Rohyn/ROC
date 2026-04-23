using UnityEngine;

/// <summary>
/// Hand-authored identity definition for a specific named NPC.
/// Use this for important settlement/service/story characters like Aidan.
/// </summary>
[CreateAssetMenu(menuName = "ROC/NPC/Named NPC Definition")]
public class NamedNPCDefinition : ScriptableObject
{
    [Header("Core Identity")]
    [SerializeField] private string npcId = "npc.new";
    [SerializeField] private string displayName = "New NPC";

    [Header("World Context")]
    [SerializeField] private WorldRegion origin = WorldRegion.Unknown;
    [SerializeField] private WorldRegion currentLocation = WorldRegion.Unknown;
    [SerializeField] private NPCRole role = NPCRole.None;

    [Header("Personality")]
    [SerializeField] private NPCPersonalityProfile personality =
        new NPCPersonalityProfile(0f, 0f, 0f, 0f, 0f);

    public string NpcId => npcId;
    public string DisplayName => displayName;
    public WorldRegion Origin => origin;
    public WorldRegion CurrentLocation => currentLocation;
    public NPCRole Role => role;
    public NPCPersonalityProfile Personality => personality;

    public NPCIdentityData ToIdentityData()
    {
        return new NPCIdentityData
        {
            npcId = npcId,
            displayName = displayName,
            identityType = NPCIdentityType.Named,
            origin = origin,
            currentLocation = currentLocation,
            role = role,
            personality = personality,
            generationSeed = 0,
            isGenerated = false
        };
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            npcId = "npc.new";
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = name;
        }
    }
}