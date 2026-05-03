using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Server-side anchor that spawns a network prefab for a manually-loaded area scene.
/// 
/// Use this for streamed-scene objects that need real Netcode state/colliders,
/// such as shared doors.
/// 
/// IMPORTANT:
/// ROC streams area scenes manually with Unity SceneManager, not Netcode scene management.
/// Because of that, dynamically spawned NetworkObjects should NOT be left in the
/// manually loaded area scene for late-join/reconnect cases.
/// 
/// This version forces the spawned NetworkObject into Unity's persistent
/// DontDestroyOnLoad scene BEFORE NetworkObject.Spawn().
/// 
/// Stable-ID interaction lookup is preserved by adding InteractableRegistrySceneOverride,
/// which makes the GenericInteractable register as if it belongs to the source area scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneNetworkObjectAnchor : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Stable anchor ID, unique within this scene. Example: dkeep.infirmary.door")]
    [SerializeField] private string anchorId;

    [Header("Prefab")]
    [Tooltip("NetworkObject prefab to spawn at this anchor. The prefab must be registered in NetworkManager Network Prefabs.")]
    [SerializeField] private NetworkObject networkPrefab;

    [Header("Spawn Transform")]
    [SerializeField] private bool useAnchorPosition = true;
    [SerializeField] private bool useAnchorRotation = true;
    [SerializeField] private bool useAnchorScale = false;

    [Header("Spawn Scene")]
    [Tooltip("Recommended ON for ROC's manually streamed area scenes. Moves the spawned prefab to DontDestroyOnLoad before Spawn().")]
    [SerializeField] private bool forceDontDestroyOnLoadBeforeSpawn = true;

    [Header("Spawn Timing")]
    [Tooltip("If true, the server spawns this prefab when the anchor starts.")]
    [SerializeField] private bool spawnOnStart = true;

    [Tooltip("Small delay after scene load before spawning.")]
    [SerializeField] private float spawnDelaySeconds = 0.05f;

    [Header("Lifecycle")]
    [Tooltip("If true, the spawned object is despawned when this anchor is destroyed or its scene unloads.")]
    [SerializeField] private bool despawnWhenAnchorDestroyed = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private NetworkObject _spawnedObject;
    private bool _spawnRoutineStarted;

    public string AnchorId => string.IsNullOrWhiteSpace(anchorId) ? name : anchorId.Trim();
    public NetworkObject SpawnedObject => _spawnedObject;

    private void Start()
    {
        if (spawnOnStart)
        {
            TryStartSpawnRoutine();
        }
    }

    private void OnDestroy()
    {
        if (!despawnWhenAnchorDestroyed)
        {
            return;
        }

        if (_spawnedObject != null &&
            _spawnedObject.IsSpawned &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsServer)
        {
            _spawnedObject.Despawn(destroy: true);
        }
    }

    [ContextMenu("Spawn Now If Server")]
    public void TryStartSpawnRoutine()
    {
        if (_spawnRoutineStarted)
        {
            return;
        }

        _spawnRoutineStarted = true;
        StartCoroutine(SpawnWhenServerReadyRoutine());
    }

    private IEnumerator SpawnWhenServerReadyRoutine()
    {
        if (spawnDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(spawnDelaySeconds);
        }
        else
        {
            yield return null;
        }

        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            yield return null;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            yield break;
        }

        SpawnServer();
    }

    private void SpawnServer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (networkPrefab == null)
        {
            Debug.LogWarning($"[SceneNetworkObjectAnchor] '{name}' has no network prefab assigned.", this);
            return;
        }

        if (_spawnedObject != null && _spawnedObject.IsSpawned)
        {
            return;
        }

        Scene anchorScene = gameObject.scene;

        if (!anchorScene.IsValid() || !anchorScene.isLoaded)
        {
            Debug.LogWarning($"[SceneNetworkObjectAnchor] Anchor '{name}' is not in a valid loaded scene.", this);
            return;
        }

        string sourceSceneName = anchorScene.name;
        Vector3 spawnPosition = useAnchorPosition ? transform.position : networkPrefab.transform.position;
        Quaternion spawnRotation = useAnchorRotation ? transform.rotation : networkPrefab.transform.rotation;

        NetworkObject instance = Instantiate(networkPrefab, spawnPosition, spawnRotation);

        if (useAnchorScale)
        {
            instance.transform.localScale = transform.lossyScale;
        }

        instance.name = $"{networkPrefab.name}_{AnchorId}";

        GenericInteractable[] interactables = instance.GetComponentsInChildren<GenericInteractable>(true);

        // Remove any immediate registration that happened during Instantiate/OnEnable.
        for (int i = 0; i < interactables.Length; i++)
        {
            if (interactables[i] != null)
            {
                InteractableRegistry.UnregisterEverywhere(interactables[i]);
            }
        }

        InteractableRegistrySceneOverride registrySceneOverride =
            instance.GetComponent<InteractableRegistrySceneOverride>();

        if (registrySceneOverride == null)
        {
            registrySceneOverride = instance.gameObject.AddComponent<InteractableRegistrySceneOverride>();
        }

        registrySceneOverride.SetSceneNameOverride(sourceSceneName);

        SceneSpawnedNetworkObjectMetadata metadata =
            instance.GetComponent<SceneSpawnedNetworkObjectMetadata>();

        if (metadata == null)
        {
            metadata = instance.gameObject.AddComponent<SceneSpawnedNetworkObjectMetadata>();
        }

        metadata.Initialize(sourceSceneName, AnchorId);

        string unitySceneBeforePersistence = instance.gameObject.scene.name;

        // Critical for manually streamed area scenes:
        // If the server's active scene is Area_DKeepCenter during direct restore,
        // Instantiate() places the object into Area_DKeepCenter. That scene is not
        // Netcode-managed, which can break late-join visibility. Move it to the
        // persistent scene before Spawn().
        if (forceDontDestroyOnLoadBeforeSpawn)
        {
            DontDestroyOnLoad(instance.gameObject);
        }

        string unitySceneBeforeSpawn = instance.gameObject.scene.name;

        instance.Spawn(destroyWithScene: false);
        _spawnedObject = instance;

        // Register under the logical/source area scene name.
        for (int i = 0; i < interactables.Length; i++)
        {
            if (interactables[i] != null)
            {
                InteractableRegistry.Register(interactables[i]);
            }
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[SceneNetworkObjectAnchor] Spawned '{instance.name}' for anchor '{AnchorId}'. " +
                $"LogicalScene='{sourceSceneName}', UnitySceneBeforePersistence='{unitySceneBeforePersistence}', " +
                $"UnitySceneBeforeSpawn='{unitySceneBeforeSpawn}', UnitySceneAfterSpawn='{instance.gameObject.scene.name}', " +
                $"DontDestroyOnLoadBeforeSpawn={forceDontDestroyOnLoadBeforeSpawn}, IsSpawned={instance.IsSpawned}",
                this);
        }
    }
}
