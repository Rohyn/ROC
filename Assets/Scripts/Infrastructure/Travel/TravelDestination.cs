using System;
using UnityEngine;

/// <summary>
/// Describes a destination that a player can travel to.
///
/// This is intentionally small for now:
/// - sceneName: which Unity scene should contain the destination
/// - spawnPointId: which SpawnPoint inside that scene should be used
///
/// Later, this can grow to include:
/// - instance identifiers
/// - travel styles
/// - camera behaviors
/// - cinematic flags
/// - arrival effects
/// </summary>
[Serializable]
public struct TravelDestination
{
    [Tooltip("The Unity scene name that contains the destination.")]
    public string sceneName;

    [Tooltip("The stable ID of the SpawnPoint inside the destination scene.")]
    public string spawnPointId;

    /// <summary>
    /// Convenience helper for readability.
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(sceneName) &&
        !string.IsNullOrWhiteSpace(spawnPointId);

    public override string ToString()
    {
        return $"{sceneName}:{spawnPointId}";
    }
}