using UnityEngine;

/// <summary>
/// Generic marker that identifies a valid spawn / arrival location inside a scene.
///
/// Examples:
/// - intro_spawn
/// - dragonkeep_infirmary_entry
/// - barracks_roof
/// - courtyard_gate
///
/// This object is NOT networked.
/// It is used by server-side travel/spawn systems to determine where a player should arrive.
///
/// This version optionally supports a SpawnArrivalProfile for richer arrival behavior,
/// such as spawning directly into a bed anchor and applying Resting.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Stable unique ID for this spawn point within its scene.")]
    [SerializeField] private string spawnPointId = "default";

    [Header("Optional Behavior")]
    [Tooltip("If true, the spawn system may align the player to this transform's rotation on arrival.")]
    [SerializeField] private bool useRotation = true;

    [Header("Arrival Profile")]
    [Tooltip("Optional arrival profile that can override initial placement and apply arrival state.")]
    [SerializeField] private SpawnArrivalProfile arrivalProfile;

    public string SpawnPointId => spawnPointId;
    public bool UseRotation => useRotation;
    public SpawnArrivalProfile ArrivalProfile => arrivalProfile;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(spawnPointId))
        {
            spawnPointId = "default";
        }

        // Convenience: if a SpawnArrivalProfile is on the same GameObject, auto-wire it.
        if (arrivalProfile == null)
        {
            arrivalProfile = GetComponent<SpawnArrivalProfile>();
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