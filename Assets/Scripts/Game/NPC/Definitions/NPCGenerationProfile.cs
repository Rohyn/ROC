using System;
using UnityEngine;

/// <summary>
/// Defines the ranges/defaults used when generating NPC identities
/// from a shared generation profile.
///
/// IMPORTANT:
/// This profile should be given a stable seed by the runtime component.
/// Do not rely on uncontrolled random generation if you want a stable NPC identity.
/// </summary>
[CreateAssetMenu(menuName = "ROC/NPC/Generation Profile")]
public class NPCGenerationProfile : ScriptableObject
{
    [Header("Defaults")]
    [SerializeField] private WorldRegion defaultOrigin = WorldRegion.Unknown;
    [SerializeField] private WorldRegion defaultCurrentLocation = WorldRegion.Unknown;
    [SerializeField] private NPCRole defaultRole = NPCRole.None;

    [Header("Personality Ranges")]
    [Range(-1f, 1f)] [SerializeField] private float minActivity = -0.5f;
    [Range(-1f, 1f)] [SerializeField] private float maxActivity = 0.5f;

    [Range(-1f, 1f)] [SerializeField] private float minSuspicion = -0.5f;
    [Range(-1f, 1f)] [SerializeField] private float maxSuspicion = 0.5f;

    [Range(-1f, 1f)] [SerializeField] private float minBravery = -0.5f;
    [Range(-1f, 1f)] [SerializeField] private float maxBravery = 0.5f;

    [Range(-1f, 1f)] [SerializeField] private float minFormality = -0.5f;
    [Range(-1f, 1f)] [SerializeField] private float maxFormality = 0.5f;

    [Range(-1f, 1f)] [SerializeField] private float minKnowledge = -0.5f;
    [Range(-1f, 1f)] [SerializeField] private float maxKnowledge = 0.5f;

    [Header("Naming")]
    [SerializeField] private string[] possibleFirstNames;

    public NPCIdentityData Generate(int stableSeed)
    {
        System.Random random = new System.Random(stableSeed);

        string generatedName = GenerateName(random);
        NPCPersonalityProfile personality = new NPCPersonalityProfile(
            NextRange(random, minActivity, maxActivity),
            NextRange(random, minSuspicion, maxSuspicion),
            NextRange(random, minBravery, maxBravery),
            NextRange(random, minFormality, maxFormality),
            NextRange(random, minKnowledge, maxKnowledge));

        return new NPCIdentityData
        {
            npcId = $"generated.{stableSeed}",
            displayName = generatedName,
            identityType = NPCIdentityType.Generated,
            origin = defaultOrigin,
            currentLocation = defaultCurrentLocation,
            role = defaultRole,
            personality = personality,
            generationSeed = stableSeed,
            isGenerated = true
        };
    }

    private string GenerateName(System.Random random)
    {
        if (possibleFirstNames == null || possibleFirstNames.Length == 0)
        {
            return "Unnamed";
        }

        int index = random.Next(0, possibleFirstNames.Length);
        return possibleFirstNames[index];
    }

    private float NextRange(System.Random random, float min, float max)
    {
        double t = random.NextDouble();
        return Mathf.Lerp(min, max, (float)t);
    }
}