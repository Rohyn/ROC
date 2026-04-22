using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// Server-authoritative travel and spawning service.
///
/// Attach this to AppRoot alongside the NetworkManager.
///
/// Responsibilities:
/// - Load the initial gameplay scene for connected clients
/// - Spawn newly connected clients at a destination
/// - Teleport players within the same scene
/// - Transfer players across scenes and place them at a spawn point
///
/// IMPORTANT:
/// This is deliberately a first-pass foundation, not a finished MMO travel system.
/// It is built to be extended later, not to solve every future travel case today.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class ServerTravelManager : MonoBehaviour
{
    [Header("Initial Flow")]
    [Tooltip("The first networked gameplay destination newly connected players should enter.")]
    [SerializeField] private TravelDestination initialDestination;

    [Header("Player Spawning")]
    [Tooltip("The player network prefab that will be spawned manually for each approved client.")]
    [SerializeField] private NetworkObject playerPrefab;

    [Tooltip("If true, the player object is destroyed when its scene unloads. Usually false for persistent players.")]
    [SerializeField] private bool destroyPlayerWithScene = false;

    private NetworkManager _networkManager;

    /// <summary>
    /// Tracks whether the initial destination scene is ready on the server.
    /// </summary>
    private bool _initialSceneReady;

    /// <summary>
    /// Clients waiting for their first player spawn after synchronization finishes.
    /// </summary>
    private readonly HashSet<ulong> _pendingInitialSpawnClients = new();

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();

        if (_networkManager == null)
        {
            Debug.LogError("[ServerTravelManager] Missing NetworkManager.");
            enabled = false;
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[ServerTravelManager] Player prefab is not assigned.");
            enabled = false;
            return;
        }

        if (!initialDestination.IsValid)
        {
            Debug.LogError("[ServerTravelManager] Initial destination is not configured.");
            enabled = false;
            return;
        }
    }

    private void OnEnable()
    {
        if (_networkManager == null)
        {
            return;
        }

        _networkManager.OnServerStarted += HandleServerStarted;

        if (_networkManager.SceneManager != null)
        {
            _networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
        }
    }

    private void OnDisable()
    {
        if (_networkManager == null)
        {
            return;
        }

        _networkManager.OnServerStarted -= HandleServerStarted;

        if (_networkManager.SceneManager != null)
        {
            _networkManager.SceneManager.OnSceneEvent -= HandleSceneEvent;
        }
    }

    /// <summary>
    /// Called when the server has started successfully.
    /// </summary>
    private void HandleServerStarted()
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        if (!_networkManager.NetworkConfig.EnableSceneManagement)
        {
            Debug.LogError("[ServerTravelManager] Enable Scene Management must be enabled on the NetworkManager.");
            return;
        }

        if (SceneManager.GetActiveScene().name == initialDestination.sceneName)
        {
            _initialSceneReady = true;
            Debug.Log($"[ServerTravelManager] Initial scene already active: {initialDestination.sceneName}");
            return;
        }

        Debug.Log($"[ServerTravelManager] Loading initial scene '{initialDestination.sceneName}'...");

        SceneEventProgressStatus status =
            _networkManager.SceneManager.LoadScene(initialDestination.sceneName, LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[ServerTravelManager] Failed to begin loading initial scene. Status: {status}");
        }
    }

    /// <summary>
    /// Handles NGO scene events.
    ///
    /// We care about:
    /// - LoadEventCompleted: tells us when a scene load has completed
    /// - SynchronizeComplete: tells us when a client is ready to receive/spawn gameplay objects
    /// </summary>
    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.LoadEventCompleted:
            {
                if (sceneEvent.SceneName == initialDestination.sceneName)
                {
                    _initialSceneReady = true;
                    Debug.Log($"[ServerTravelManager] Initial scene '{initialDestination.sceneName}' is ready.");
                    TrySpawnPendingInitialPlayers();
                }

                break;
            }

            case SceneEventType.SynchronizeComplete:
            {
                ulong clientId = sceneEvent.ClientId;

                if (clientId == NetworkManager.ServerClientId)
                {
                    return;
                }

                Debug.Log($"[ServerTravelManager] Client {clientId} finished synchronization.");

                _pendingInitialSpawnClients.Add(clientId);
                TrySpawnPendingInitialPlayers();

                break;
            }
        }
    }

    /// <summary>
    /// Attempts to spawn PlayerObjects for any clients waiting on their first spawn.
    /// </summary>
    private void TrySpawnPendingInitialPlayers()
    {
        if (!_initialSceneReady || _pendingInitialSpawnClients.Count == 0)
        {
            return;
        }

        List<ulong> handledClients = new();

        foreach (ulong clientId in _pendingInitialSpawnClients)
        {
            if (!_networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            {
                handledClients.Add(clientId);
                continue;
            }

            if (client.PlayerObject != null)
            {
                handledClients.Add(clientId);
                continue;
            }

            bool spawned = SpawnPlayerForClient(clientId, initialDestination);
            if (spawned)
            {
                handledClients.Add(clientId);
            }
        }

        foreach (ulong clientId in handledClients)
        {
            _pendingInitialSpawnClients.Remove(clientId);
        }
    }

    /// <summary>
    /// Spawns a player's PlayerObject at a destination.
    ///
    /// Use this only when the client does not already have a PlayerObject.
    /// </summary>
    public bool SpawnPlayerForClient(ulong clientId, TravelDestination destination)
    {
        if (!_networkManager.IsServer)
        {
            Debug.LogWarning("[ServerTravelManager] SpawnPlayerForClient was called on a non-server instance.");
            return false;
        }

        if (!TryResolveSpawnPoint(destination, out SpawnPoint spawnPoint))
        {
            Debug.LogError($"[ServerTravelManager] Could not resolve spawn point for destination {destination}.");
            return false;
        }

        Vector3 position = spawnPoint.transform.position;
        Quaternion rotation = spawnPoint.UseRotation ? spawnPoint.transform.rotation : Quaternion.identity;

        NetworkObject playerInstance = Instantiate(playerPrefab, position, rotation);

        // Recommended for objects that should follow active-scene changes later.
        playerInstance.ActiveSceneSynchronization = true;

        // SceneMigrationSynchronization is enabled by default, but setting it explicitly
        // makes the intention clearer for future maintenance.
        playerInstance.SceneMigrationSynchronization = true;

        playerInstance.SpawnAsPlayerObject(clientId, destroyPlayerWithScene);

        Debug.Log($"[ServerTravelManager] Spawned player for ClientId {clientId} at {destination}.");
        return true;
    }

    /// <summary>
    /// Teleports an already-spawned player within the current scene.
    ///
    /// Use this for:
    /// - ladders
    /// - short-range teleporters
    /// - interior repositioning
    /// - return-to-door effects within the same scene
    /// </summary>
    public bool TeleportPlayerInCurrentScene(NetworkObject playerObject, string spawnPointId)
    {
        if (!_networkManager.IsServer)
        {
            Debug.LogWarning("[ServerTravelManager] TeleportPlayerInCurrentScene was called on a non-server instance.");
            return false;
        }

        if (playerObject == null)
        {
            Debug.LogError("[ServerTravelManager] Teleport failed because playerObject is null.");
            return false;
        }

        Scene currentScene = playerObject.gameObject.scene;

        if (!TryFindSpawnPointInScene(currentScene, spawnPointId, out SpawnPoint spawnPoint))
        {
            Debug.LogError($"[ServerTravelManager] Could not find spawn point '{spawnPointId}' in scene '{currentScene.name}'.");
            return false;
        }

        playerObject.transform.SetPositionAndRotation(
            spawnPoint.transform.position,
            spawnPoint.UseRotation ? spawnPoint.transform.rotation : playerObject.transform.rotation);

        Debug.Log($"[ServerTravelManager] Teleported player {playerObject.OwnerClientId} within scene '{currentScene.name}' to '{spawnPointId}'.");
        return true;
    }

    /// <summary>
    /// Transfers an already-spawned player into another scene and places them at a destination.
    ///
    /// This is the generic foundation for:
    /// - entering/leaving the Intro
    /// - portals between larger zones
    /// - recall/home travel
    /// - settlement/world transitions
    ///
    /// IMPORTANT:
    /// This method assumes the destination scene is already loaded by the server.
    /// In your first phase, that is fine. Later, you can extend this to load scenes on demand.
    /// </summary>
    public bool TransferPlayerToLoadedScene(NetworkObject playerObject, TravelDestination destination)
    {
        if (!_networkManager.IsServer)
        {
            Debug.LogWarning("[ServerTravelManager] TransferPlayerToLoadedScene was called on a non-server instance.");
            return false;
        }

        if (playerObject == null)
        {
            Debug.LogError("[ServerTravelManager] Transfer failed because playerObject is null.");
            return false;
        }

        if (!TryResolveSpawnPoint(destination, out SpawnPoint spawnPoint))
        {
            Debug.LogError($"[ServerTravelManager] Could not resolve destination {destination}.");
            return false;
        }

        Scene destinationScene = spawnPoint.gameObject.scene;

        // Move the player's NetworkObject into the target Unity scene.
        SceneManager.MoveGameObjectToScene(playerObject.gameObject, destinationScene);

        // Then set the player's arrival transform.
        playerObject.transform.SetPositionAndRotation(
            spawnPoint.transform.position,
            spawnPoint.UseRotation ? spawnPoint.transform.rotation : playerObject.transform.rotation);

        Debug.Log($"[ServerTravelManager] Transferred player {playerObject.OwnerClientId} to {destination}.");
        return true;
    }

    /// <summary>
    /// Resolves a TravelDestination into an actual SpawnPoint object in a loaded scene.
    /// </summary>
    private bool TryResolveSpawnPoint(TravelDestination destination, out SpawnPoint spawnPoint)
    {
        spawnPoint = null;

        Scene destinationScene = SceneManager.GetSceneByName(destination.sceneName);
        if (!destinationScene.IsValid() || !destinationScene.isLoaded)
        {
            Debug.LogWarning($"[ServerTravelManager] Scene '{destination.sceneName}' is not loaded.");
            return false;
        }

        return TryFindSpawnPointInScene(destinationScene, destination.spawnPointId, out spawnPoint);
    }

    /// <summary>
    /// Finds a SpawnPoint with the requested ID inside a specific scene.
    /// </summary>
    private bool TryFindSpawnPointInScene(Scene scene, string spawnPointId, out SpawnPoint spawnPoint)
    {
        spawnPoint = null;

        GameObject[] rootObjects = scene.GetRootGameObjects();
        foreach (GameObject root in rootObjects)
        {
            SpawnPoint[] found = root.GetComponentsInChildren<SpawnPoint>(true);
            foreach (SpawnPoint candidate in found)
            {
                if (candidate.SpawnPointId == spawnPointId)
                {
                    spawnPoint = candidate;
                    return true;
                }
            }
        }

        return false;
    }
}