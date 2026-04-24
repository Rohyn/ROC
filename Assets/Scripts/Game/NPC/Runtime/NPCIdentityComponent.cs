using UnityEngine;

/// <summary>
/// Resolves and stores the runtime identity for an NPC instance.
/// Supports both named NPC definitions and generated identities.
///
/// This component is the runtime "identity surface" the rest of the NPC
/// and conversation systems should read from.
/// </summary>
public class NPCIdentityComponent : MonoBehaviour
{
    [Header("Identity Source")]
    [SerializeField] private NPCIdentityType identityType = NPCIdentityType.Named;
    [SerializeField] private NamedNPCDefinition namedDefinition;
    [SerializeField] private NPCGenerationProfile generationProfile;

    [Header("Generated Identity")]
    [Tooltip("Stable seed used when generating this NPC's identity. Required for generated NPCs.")]
    [SerializeField] private int generationSeed = 1;

    [Header("Overrides")]
    [SerializeField] private bool overrideCurrentLocation;
    [SerializeField] private WorldRegion overriddenCurrentLocation = WorldRegion.Unknown;

    [Header("Runtime")]
    [SerializeField] private NPCIdentityData identityData;

    public NPCIdentityData Identity => identityData;
    public string DisplayName => identityData != null ? identityData.displayName : "Unknown";
    public NPCRole Role => identityData != null ? identityData.role : NPCRole.None;
    public WorldRegion Origin => identityData != null ? identityData.origin : WorldRegion.Unknown;
    public WorldRegion CurrentLocation => identityData != null ? identityData.currentLocation : WorldRegion.Unknown;
    public NPCPersonalityProfile Personality => identityData != null
        ? identityData.personality
        : new NPCPersonalityProfile(0f, 0f, 0f, 0f, 0f);
    public string NpcId => identityData != null ? identityData.npcId : string.Empty;

    private void Awake()
    {
        ResolveIdentity();
    }

    public void ResolveIdentity()
    {
        switch (identityType)
        {
            case NPCIdentityType.Named:
                ResolveNamedIdentity();
                break;

            case NPCIdentityType.Generated:
                ResolveGeneratedIdentity();
                break;
        }

        ApplyOverrides();
    }

    private void ResolveNamedIdentity()
    {
        if (namedDefinition == null)
        {
            identityData = new NPCIdentityData
            {
                npcId = "missing.named.definition",
                displayName = "Unnamed NPC",
                identityType = NPCIdentityType.Named,
                origin = WorldRegion.Unknown,
                currentLocation = WorldRegion.Unknown,
                role = NPCRole.None,
                personality = new NPCPersonalityProfile(0f, 0f, 0f, 0f, 0f),
                generationSeed = 0,
                isGenerated = false
            };

            return;
        }

        identityData = namedDefinition.ToIdentityData();
    }

    private void ResolveGeneratedIdentity()
    {
        if (generationProfile == null)
        {
            identityData = new NPCIdentityData
            {
                npcId = "missing.generation.profile",
                displayName = "Generated NPC",
                identityType = NPCIdentityType.Generated,
                origin = WorldRegion.Unknown,
                currentLocation = WorldRegion.Unknown,
                role = NPCRole.None,
                personality = new NPCPersonalityProfile(0f, 0f, 0f, 0f, 0f),
                generationSeed = 0,
                isGenerated = true
            };

            return;
        }

        if (generationSeed == 0)
        {
            Debug.LogWarning($"[NPCIdentityComponent] Generated NPC '{name}' has generationSeed = 0. Assign a stable seed for deterministic generation.", this);
        }

        identityData = generationProfile.Generate(generationSeed);
    }

    private void ApplyOverrides()
    {
        if (identityData == null)
        {
            return;
        }

        if (overrideCurrentLocation)
        {
            identityData.currentLocation = overriddenCurrentLocation;
        }
    }
}