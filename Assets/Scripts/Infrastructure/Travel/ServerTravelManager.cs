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
/// - Load the initial gameplay scene after the dedicated server starts
/// - Wait for connected clients to fully synchronize into the session
/// - Manually spawn each client's PlayerObject at a configured destination
/// - Provide reusable methods for same-scene teleports and cross-scene transfers
///
/// This is intended to be a reusable foundation, not an Intro-only script.
/// The Intro is simply the first configured destination.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class ServerTravelManager : MonoBehaviour
{
    [Header("Initial Flow")]
    [Tooltip("The first networked gameplay destination newly connected players should enter.")]
    [SerializeField] private TravelDestination initialDestination;

    [Header("Player Spawning")]
    [Tooltip("The networked player prefab that will be spawned manually for each connected client.")]
    [SerializeField] private NetworkObject playerPrefab;

    [Tooltip("If true, the player object is destroyed when its scene unloads. Usually false for persistent player objects.")]
    [SerializeField] private bool destroyPlayerWithScene = false;

    // Cached reference to the local NetworkManager.
    private NetworkManager _networkManager;

    /// <summary>
    /// Tracks whether the initial destination scene is fully ready on the server.
    /// We only want to spawn players after the server-side scene is loaded and ready.
    /// </summary>
    private bool _initialSceneReady;

    /// <summary>
    /// Tracks whether we have successfully subscribed to NGO scene events.
    /// We subscribe only after the server has started, because SceneManager may not
    /// be ready earlier.
    /// </summary>
    private bool _sceneEventsSubscribed;

    /// <summary>
    /// Client IDs waiting for their first PlayerObject spawn.
    /// A client gets added here after NGO reports SynchronizeComplete.
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

        // Safe to subscribe before server startup.
        // The server-start callback tells us when NGO is fully running.
        _networkManager.OnServerStarted += HandleServerStarted;
    }

    private void OnDisable()
    {
        if (_networkManager == null)
        {
            return;
        }

        _networkManager.OnServerStarted -= HandleServerStarted;

        // Unsubscribe from scene events if we were subscribed.
        if (_sceneEventsSubscribed && _networkManager.SceneManager != null)
        {
            _networkManager.SceneManager.OnSceneEvent -= HandleSceneEvent;
            _sceneEventsSubscribed = false;
        }
    }

    /// <summary>
    /// Called once the server has successfully started listening.
    ///
    /// This is the correct time to:
    /// - confirm scene management is enabled
    /// - subscribe to NGO scene events
    /// - set synchronization mode
    /// - load the initial gameplay scene
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

        if (_networkManager.SceneManager == null)
        {
            Debug.LogError("[ServerTravelManager] NetworkManager.SceneManager is null after server start.");
            return;
        }

        // Subscribe now, after NGO is fully started.
        if (!_sceneEventsSubscribed)
        {
            _networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
            _sceneEventsSubscribed = true;
            Debug.Log("[ServerTravelManager] Subscribed to NetworkSceneManager.OnSceneEvent.");
        }

        // Be explicit about synchronization behavior.
        // For now, Single mode is the correct choice:
        // when a client connects, NGO will sync them into the server's active scene.
        _networkManager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Single);

        // If the active scene is already the intended initial scene, mark it ready.
        if (SceneManager.GetActiveScene().name == initialDestination.sceneName)
        {
            _initialSceneReady = true;
            Debug.Log($"[ServerTravelManager] Initial scene already active: {initialDestination.sceneName}");
            TrySpawnPendingInitialPlayers();
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
    /// We primarily care about:
    /// - LoadEventCompleted: tells us the initial gameplay scene finished loading
    /// - SynchronizeComplete: tells us a client finished scene/object synchronization
    /// </summary>
    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        Debug.Log($"[ServerTravelManager] Scene event received. Type={sceneEvent.SceneEventType}, Scene={sceneEvent.SceneName}, ClientId={sceneEvent.ClientId}");

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.LoadEventCompleted:
            {
                // Once the initial scene finishes loading, mark it ready and set it active.
                if (sceneEvent.SceneName == initialDestination.sceneName)
                {
                    Scene targetScene = SceneManager.GetSceneByName(initialDestination.sceneName);

                    if (targetScene.IsValid() && targetScene.isLoaded)
                    {
                        bool activeSet = SceneManager.SetActiveScene(targetScene);
                        Debug.Log($"[ServerTravelManager] SetActiveScene('{initialDestination.sceneName}') returned {activeSet}.");
                    }
                    else
                    {
                        Debug.LogWarning($"[ServerTravelManager] Scene '{initialDestination.sceneName}' was reported loaded, but could not be resolved as a valid loaded scene.");
                    }

                    _initialSceneReady = true;
                    Debug.Log($"[ServerTravelManager] Initial scene '{initialDestination.sceneName}' is ready.");

                    // In case any clients already finished synchronization while we were waiting,
                    // attempt to spawn them now.
                    TrySpawnPendingInitialPlayers();
                }

                break;
            }

            case SceneEventType.SynchronizeComplete:
            {
                ulong clientId = sceneEvent.ClientId;

                // Skip the dedicated server itself.
                if (clientId == NetworkManager.ServerClientId)
                {
                    return;
                }

                Debug.Log($"[ServerTravelManager] Client {clientId} finished synchronization.");

                // Add this client to the pending first-spawn list.
                _pendingInitialSpawnClients.Add(clientId);

                // If the scene is already ready, this may spawn immediately.
                TrySpawnPendingInitialPlayers();

                break;
            }
        }
    }

    /// <summary>
    /// Attempts to spawn PlayerObjects for any connected clients waiting on their first spawn.
    ///
    /// A client will only be spawned if:
    /// - the initial scene is ready
    /// - the client is still connected
    /// - the client does not already have a PlayerObject
    /// </summary>
    private void TrySpawnPendingInitialPlayers()
    {
        Debug.Log($"[ServerTravelManager] TrySpawnPendingInitialPlayers called. initialSceneReady={_initialSceneReady}, pendingCount={_pendingInitialSpawnClients.Count}");

        if (!_initialSceneReady || _pendingInitialSpawnClients.Count == 0)
        {
            return;
        }

        List<ulong> handledClients = new();

        foreach (ulong clientId in _pendingInitialSpawnClients)
        {
            // Make sure the client is still connected.
            if (!_networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            {
                handledClients.Add(clientId);
                continue;
            }

            // If the client already has a PlayerObject, do not create another one.
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

        // Remove successfully handled clients from the pending set.
        foreach (ulong clientId in handledClients)
        {
            _pendingInitialSpawnClients.Remove(clientId);
        }
    }

    /// <summary>
    /// Spawns a new PlayerObject for a client at a specific destination.
    ///
    /// This should only be used when the client does NOT already have a PlayerObject.
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

        Vector3 spawnPosition = spawnPoint.transform.position;
        Quaternion spawnRotation = spawnPoint.UseRotation
            ? spawnPoint.transform.rotation
            : Quaternion.identity;

        NetworkObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);

        // These settings make later scene travel easier.
        // ActiveSceneSynchronization:
        // - if the active scene changes later, this object can follow automatically.
        //
        // SceneMigrationSynchronization:
        // - if we manually move the object to another scene later, NGO can synchronize that move.
        playerInstance.ActiveSceneSynchronization = true;
        playerInstance.SceneMigrationSynchronization = true;

        // Register this object as the official PlayerObject for the specified client.
        playerInstance.SpawnAsPlayerObject(clientId, destroyPlayerWithScene);

        Debug.Log($"[ServerTravelManager] Spawned player for ClientId {clientId} at {destination}.");
        return true;
    }

    /// <summary>
    /// Teleports an already-spawned player within their current scene.
    ///
    /// Use this for:
    /// - ladders
    /// - same-scene portals
    /// - short-range magical repositioning
    /// - roof access
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

        Quaternion rotationToUse = spawnPoint.UseRotation
            ? spawnPoint.transform.rotation
            : playerObject.transform.rotation;

        playerObject.transform.SetPositionAndRotation(
            spawnPoint.transform.position,
            rotationToUse);

        Debug.Log($"[ServerTravelManager] Teleported player {playerObject.OwnerClientId} within scene '{currentScene.name}' to '{spawnPointId}'.");
        return true;
    }

    /// <summary>
    /// Transfers an already-spawned player to a destination in another loaded scene.
    ///
    /// IMPORTANT:
    /// This first-pass implementation assumes the destination scene is already loaded
    /// on the server. That is enough for now.
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

        // Move the player's GameObject into the target scene on the server.
        // Because SceneMigrationSynchronization is enabled on the NetworkObject,
        // NGO can synchronize this move to connected clients.
        SceneManager.MoveGameObjectToScene(playerObject.gameObject, destinationScene);

        Quaternion rotationToUse = spawnPoint.UseRotation
            ? spawnPoint.transform.rotation
            : playerObject.transform.rotation;

        playerObject.transform.SetPositionAndRotation(
            spawnPoint.transform.position,
            rotationToUse);

        Debug.Log($"[ServerTravelManager] Transferred player {playerObject.OwnerClientId} to {destination}.");
        return true;
    }

    /// <summary>
    /// Resolves a TravelDestination into a concrete SpawnPoint inside a loaded scene.
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
    /// Searches a specific scene for a SpawnPoint with the requested ID.
    /// </summary>
    private bool TryFindSpawnPointInScene(Scene scene, string spawnPointId, out SpawnPoint spawnPoint)
    {
        spawnPoint = null;

        GameObject[] rootObjects = scene.GetRootGameObjects();

        foreach (GameObject root in rootObjects)
        {
            SpawnPoint[] foundSpawnPoints = root.GetComponentsInChildren<SpawnPoint>(true);

            foreach (SpawnPoint candidate in foundSpawnPoints)
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