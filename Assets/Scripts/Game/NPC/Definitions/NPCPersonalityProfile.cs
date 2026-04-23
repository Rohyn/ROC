using System;
using UnityEngine;

/// <summary>
/// Personality axes used to influence future dialogue tone, reliability,
/// social behavior, and available interactions.
///
/// Values are normalized from -1 to 1.
/// </summary>
[Serializable]
public struct NPCPersonalityProfile
{
    [Range(-1f, 1f)] public float activity;
    [Range(-1f, 1f)] public float suspicion;
    [Range(-1f, 1f)] public float bravery;
    [Range(-1f, 1f)] public float formality;
    [Range(-1f, 1f)] public float knowledge;

    public NPCPersonalityProfile(
        float activity,
        float suspicion,
        float bravery,
        float formality,
        float knowledge)
    {
        this.activity = Mathf.Clamp(activity, -1f, 1f);
        this.suspicion = Mathf.Clamp(suspicion, -1f, 1f);
        this.bravery = Mathf.Clamp(bravery, -1f, 1f);
        this.formality = Mathf.Clamp(formality, -1f, 1f);
        this.knowledge = Mathf.Clamp(knowledge, -1f, 1f);
    }
}