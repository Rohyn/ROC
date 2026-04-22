using UnityEngine;

/// <summary>
/// Generic marker that identifies a valid spawn / arrival location inside a scene.
///
/// Examples:
/// - intro_bed
/// - dragonkeep_infirmary_entry
/// - barracks_roof
/// - courtyard_gate
///
/// This object is NOT networked.
/// It is simply a server-side lookup marker used when deciding where to place
/// a player's networked character.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Stable unique ID for this spawn point within its scene.")]
    [SerializeField] private string spawnPointId = "default";

    [Header("Optional Behavior")]
    [Tooltip("If true, the travel system may align the player to this transform's rotation on arrival.")]
    [SerializeField] private bool useRotation = true;

    public string SpawnPointId => spawnPointId;
    public bool UseRotation => useRotation;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(spawnPointId))
        {
            spawnPointId = "default";
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;

        Vector3 basePos = transform.position;
        Vector3 topPos = basePos + Vector3.up * 1.8f;

        Gizmos.DrawWireSphere(basePos + Vector3.up * 0.1f, 0.2f);
        Gizmos.DrawLine(basePos, topPos);
        Gizmos.DrawLine(topPos, topPos + transform.forward * 0.75f);
    }
}