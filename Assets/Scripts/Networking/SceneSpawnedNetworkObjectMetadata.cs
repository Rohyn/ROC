using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lightweight metadata/debug component for objects spawned by SceneNetworkObjectAnchor.
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneSpawnedNetworkObjectMetadata : NetworkBehaviour
{
    [SerializeField] private string sourceSceneName;
    [SerializeField] private string sourceAnchorId;

    [Header("Debug")]
    [SerializeField] private bool logRendererStateOnSpawn = true;

    public string SourceSceneName => sourceSceneName;
    public string SourceAnchorId => sourceAnchorId;

    public void Initialize(string sceneName, string anchorId)
    {
        sourceSceneName = sceneName ?? string.Empty;
        sourceAnchorId = anchorId ?? string.Empty;
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log(
            $"[SceneSpawnedNetworkObjectMetadata] Spawned '{name}'. " +
            $"Source={sourceSceneName}:{sourceAnchorId}, " +
            $"Scene='{gameObject.scene.name}', IsServer={IsServer}, IsClient={IsClient}, IsOwner={IsOwner}",
            this);

        if (logRendererStateOnSpawn)
        {
            StartCoroutine(LogRendererStateAfterSpawnRoutine());
        }
    }

    private IEnumerator LogRendererStateAfterSpawnRoutine()
    {
        yield return null;
        yield return null;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Collider[] colliders = GetComponentsInChildren<Collider>(true);

        int enabledRendererCount = 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled && renderers[i].gameObject.activeInHierarchy)
            {
                enabledRendererCount++;
            }
        }

        Debug.Log(
            $"[SceneSpawnedNetworkObjectMetadata] '{name}' render/collider check: " +
            $"renderers={renderers.Length}, enabledActiveRenderers={enabledRendererCount}, colliders={colliders.Length}, " +
            $"scene='{gameObject.scene.name}'.",
            this);
    }
}
