using System.Collections.Generic;
using ROC.Persistence;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Server-authoritative travel and spawning service.
///
/// Attach this to AppRoot alongside the NetworkManager.
///
/// Persistence behavior:
/// - New characters receive the configured initial SpawnArrivalProfile.
/// - Returning characters load their saved state through PlayerPersistenceRoot
///   and skip the initial SpawnArrivalProfile.
/// - Player objects are persistent. Area scenes stream around them.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class ServerTravelManager : MonoBehaviour
{
    [Header("Initial Flow")]
    [Tooltip("The first networked gameplay destination newly connected players should enter.")]
    [SerializeField] private TravelDestination initialDestination;

    [Header("Preloaded Gameplay Scenes")]
    [Tooltip("Additional gameplay scenes that should always be loaded additively after the initial scene is ready.")]
    [SerializeField] private string[] additionalStartupScenes;

    [Header("Player Spawning")]
    [Tooltip("The networked player prefab that will be spawned manually for each connected client.")]
    [SerializeField] private NetworkObject playerPrefab;

    [Tooltip("If true, the player object is destroyed when its scene unloads. Usually false for persistent player objects.")]
    [SerializeField] private bool destroyPlayerWithScene = false;

    private NetworkManager _networkManager;
    private bool _initialSceneReady;
    private bool _sceneEventsSubscribed;

    private readonly HashSet<ulong> _pendingInitialSpawnClients = new();
    private readonly HashSet<string> _startupScenesRequested = new();

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
        }
    }

    private void OnEnable()
    {
        if (_networkManager == null)
        {
            return;
        }

        _networkManager.OnServerStarted += HandleServerStarted;
    }

    private void OnDisable()
    {
        if (_networkManager == null)
        {
            return;
        }

        _networkManager.OnServerStarted -= HandleServerStarted;

        if (_sceneEventsSubscribed && _networkManager.SceneManager != null)
        {
            _networkManager.SceneManager.OnSceneEvent -= HandleSceneEvent;
            _sceneEventsSubscribed = false;
        }
    }

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

        if (!_sceneEventsSubscribed)
        {
            _networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
            _sceneEventsSubscribed = true;

            Debug.Log("[ServerTravelManager] Subscribed to NetworkSceneManager.OnSceneEvent.");
        }

        _networkManager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Single);

        if (SceneManager.GetActiveScene().name == initialDestination.sceneName)
        {
            _initialSceneReady = true;

            Debug.Log($"[ServerTravelManager] Initial scene already active: {initialDestination.sceneName}");

            EnsureAdditionalStartupScenesLoaded();
            TrySpawnPendingInitialPlayers();
            return;
        }

        Debug.Log($"[ServerTravelManager] Loading initial scene '{initialDestination.sceneName}'...");

        SceneEventProgressStatus status =
            _networkManager.SceneManager.LoadScene(initialDestination.sceneName, LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError(
                $"[ServerTravelManager] Failed to begin loading initial scene. Status: {status}");
        }
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        Debug.Log(
            $"[ServerTravelManager] Scene event received. Type={sceneEvent.SceneEventType}, Scene={sceneEvent.SceneName}, ClientId={sceneEvent.ClientId}");

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.LoadEventCompleted:
            {
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
                        Debug.LogWarning(
                            $"[ServerTravelManager] Scene '{initialDestination.sceneName}' was reported loaded, but could not be resolved as a valid loaded scene.");
                    }

                    _initialSceneReady = true;

                    Debug.Log($"[ServerTravelManager] Initial scene '{initialDestination.sceneName}' is ready.");

                    EnsureAdditionalStartupScenesLoaded();
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

    private void EnsureAdditionalStartupScenesLoaded()
    {
        if (!_networkManager.IsServer || !_initialSceneReady)
        {
            return;
        }

        if (additionalStartupScenes == null || additionalStartupScenes.Length == 0)
        {
            return;
        }

        for (int i = 0; i < additionalStartupScenes.Length; i++)
        {
            string sceneName = additionalStartupScenes[i];

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                continue;
            }

            if (sceneName == initialDestination.sceneName)
            {
                continue;
            }

            Scene scene = SceneManager.GetSceneByName(sceneName);

            if (scene.IsValid() && scene.isLoaded)
            {
                continue;
            }

            if (_startupScenesRequested.Contains(sceneName))
            {
                continue;
            }

            SceneEventProgressStatus status =
                _networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);

            if (status == SceneEventProgressStatus.Started)
            {
                _startupScenesRequested.Add(sceneName);

                Debug.Log($"[ServerTravelManager] Loading additional startup scene '{sceneName}' additively.");
            }
            else
            {
                Debug.LogError(
                    $"[ServerTravelManager] Failed to begin loading additional startup scene '{sceneName}'. Status: {status}");
            }
        }
    }

    private void TrySpawnPendingInitialPlayers()
    {
        Debug.Log(
            $"[ServerTravelManager] TrySpawnPendingInitialPlayers called. initialSceneReady={_initialSceneReady}, pendingCount={_pendingInitialSpawnClients.Count}");

        if (!_initialSceneReady || _pendingInitialSpawnClients.Count == 0)
        {
            return;
        }

        List<ulong> handledClients = new List<ulong>();

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

        SpawnArrivalProfile arrivalProfile = spawnPoint.ArrivalProfile;

        Vector3 spawnPosition = spawnPoint.transform.position;
        Quaternion spawnRotation = spawnPoint.UseRotation ? spawnPoint.transform.rotation : Quaternion.identity;

        if (arrivalProfile != null)
        {
            arrivalProfile.GetInitialSpawnPose(
                spawnPosition,
                spawnRotation,
                out spawnPosition,
                out spawnRotation);
        }

        NetworkObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);

        playerInstance.ActiveSceneSynchronization = false;
        playerInstance.SceneMigrationSynchronization = false;

        playerInstance.SpawnAsPlayerObject(clientId, destroyPlayerWithScene);

        PlayerPersistenceRoot persistenceRoot = playerInstance.GetComponent<PlayerPersistenceRoot>();
        bool isReturningCharacter = persistenceRoot != null && persistenceRoot.LoadedExistingCharacter;

        if (!isReturningCharacter && arrivalProfile != null)
        {
            arrivalProfile.ApplyToPlayer(playerInstance.gameObject);

            Debug.Log($"[ServerTravelManager] Applied initial arrival profile for new character on ClientId {clientId}.");
        }
        else if (isReturningCharacter)
        {
            Debug.Log(
                $"[ServerTravelManager] Skipped initial arrival profile for returning character on ClientId {clientId}. SavedScene='{persistenceRoot.LoadedCharacterSceneId}'.");
        }

        Debug.Log($"[ServerTravelManager] Spawned player for ClientId {clientId} at {destination}.");

        return true;
    }

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
            Debug.LogError(
                $"[ServerTravelManager] Could not find spawn point '{spawnPointId}' in scene '{currentScene.name}'.");
            return false;
        }

        Quaternion rotationToUse = spawnPoint.UseRotation
            ? spawnPoint.transform.rotation
            : playerObject.transform.rotation;

        playerObject.transform.SetPositionAndRotation(
            spawnPoint.transform.position,
            rotationToUse);

        Debug.Log(
            $"[ServerTravelManager] Teleported player {playerObject.OwnerClientId} within scene '{currentScene.name}' to '{spawnPointId}'.");

        return true;
    }

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

        Quaternion rotationToUse = spawnPoint.UseRotation
            ? spawnPoint.transform.rotation
            : playerObject.transform.rotation;

        playerObject.transform.SetPositionAndRotation(
            spawnPoint.transform.position,
            rotationToUse);

        Debug.Log($"[ServerTravelManager] Transferred player {playerObject.OwnerClientId} to {destination}.");

        return true;
    }

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

    private bool TryFindSpawnPointInScene(Scene scene, string spawnPointId, out SpawnPoint spawnPoint)
    {
        spawnPoint = null;

        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

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