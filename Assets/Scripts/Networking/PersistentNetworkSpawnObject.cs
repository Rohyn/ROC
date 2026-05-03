using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach this to the ROOT of any dynamically spawned NetworkObject that must survive
/// local client scene cleanup after it is spawned.
///
/// ROC streams area scenes manually. A dynamic network prefab can be instantiated on
/// the client into whatever Unity scene is active at the moment of spawn. If that scene
/// is later unloaded by PlayerAreaStreamingController, the client-side presentation can
/// disappear even though the server-side collider/state still exists.
///
/// This component moves the spawned object into DontDestroyOnLoad on every instance
/// during OnNetworkSpawn, including clients.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PersistentNetworkSpawnObject : NetworkBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    [Tooltip("Logs renderer/collider state a few frames after spawn.")]
    [SerializeField] private bool logRendererAndColliderState = true;

    public override void OnNetworkSpawn()
    {
        string beforeSceneName = gameObject.scene.IsValid()
            ? gameObject.scene.name
            : "<invalid>";

        DontDestroyOnLoad(gameObject);

        string afterSceneName = gameObject.scene.IsValid()
            ? gameObject.scene.name
            : "<invalid>";

        if (verboseLogging)
        {
            Debug.Log(
                $"[PersistentNetworkSpawnObject] '{name}' moved to persistent scene on OnNetworkSpawn. " +
                $"Before='{beforeSceneName}', After='{afterSceneName}', " +
                $"IsServer={IsServer}, IsClient={IsClient}, IsOwner={IsOwner}.",
                this);
        }

        if (logRendererAndColliderState)
        {
            StartCoroutine(LogRendererAndColliderStateRoutine());
        }
    }

    private IEnumerator LogRendererAndColliderStateRoutine()
    {
        yield return null;
        yield return null;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Collider[] colliders = GetComponentsInChildren<Collider>(true);

        int enabledActiveRenderers = 0;
        int enabledActiveColliders = 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];

            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
            {
                enabledActiveRenderers++;
            }
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];

            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
            {
                enabledActiveColliders++;
            }
        }

        Debug.Log(
            $"[PersistentNetworkSpawnObject] '{name}' render/collider check: " +
            $"renderers={renderers.Length}, enabledActiveRenderers={enabledActiveRenderers}, " +
            $"colliders={colliders.Length}, enabledActiveColliders={enabledActiveColliders}, " +
            $"scene='{gameObject.scene.name}', position={transform.position}, layer={gameObject.layer}.",
            this);
    }
}
