using System;
using UnityEngine;

/// <summary>
/// Resolved runtime identity data for a single NPC instance.
/// This is what conversation and future systems should read from.
/// </summary>
[Serializable]
public class NPCIdentityData
{
    [Header("Core Identity")]
    public string npcId;
    public string displayName;
    public NPCIdentityType identityType;

    [Header("World Context")]
    public WorldRegion origin;
    public WorldRegion currentLocation;
    public NPCRole role;

    [Header("Personality")]
    public NPCPersonalityProfile personality;

    [Header("Generation")]
    public int generationSeed;
    public bool isGenerated;
}