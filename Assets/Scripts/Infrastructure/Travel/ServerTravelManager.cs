using System.Collections;
using System.Collections.Generic;
using ROC.Persistence;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkManager))]
public class ServerTravelManager : MonoBehaviour
{
    [Header("Initial Flow")]
    [SerializeField] private TravelDestination initialDestination;

    [Header("Preloaded Gameplay Scenes")]
    [SerializeField] private string[] additionalStartupScenes;

    [Header("Player Spawning")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private bool destroyPlayerWithScene = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private NetworkManager _networkManager;
    private bool _initialSceneReady;
    private bool _sceneEventsSubscribed;

    private readonly HashSet<ulong> _spawnRoutinesInProgress = new();
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
            EnsureAdditionalStartupScenesLoaded();
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

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[ServerTravelManager] Scene event received. Type={sceneEvent.SceneEventType}, Scene={sceneEvent.SceneName}, ClientId={sceneEvent.ClientId}");
        }

        if (sceneEvent.SceneEventType != SceneEventType.LoadEventCompleted)
        {
            return;
        }

        if (sceneEvent.SceneName != initialDestination.sceneName)
        {
            return;
        }

        Scene targetScene = SceneManager.GetSceneByName(initialDestination.sceneName);

        if (targetScene.IsValid() && targetScene.isLoaded)
        {
            bool activeSet = SceneManager.SetActiveScene(targetScene);
            Debug.Log($"[ServerTravelManager] SetActiveScene('{initialDestination.sceneName}') returned {activeSet}.");
        }

        _initialSceneReady = true;

        Debug.Log($"[ServerTravelManager] Initial scene '{initialDestination.sceneName}' is ready.");

        EnsureAdditionalStartupScenesLoaded();
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

    public bool BeginSpawnDefaultCharacterForClient(ulong clientId)
    {
        PlayerPersistenceRoot prefabPersistenceRoot = playerPrefab.GetComponent<PlayerPersistenceRoot>();

        if (prefabPersistenceRoot == null)
        {
            Debug.LogError("[ServerTravelManager] Player prefab is missing PlayerPersistenceRoot.");
            return false;
        }

        string accountId = prefabPersistenceRoot.GetAccountIdForClient(clientId);
        string characterId = prefabPersistenceRoot.GetCharacterIdForClient(clientId);

        return BeginSpawnSelectedCharacterForClient(clientId, accountId, characterId);
    }

    public bool BeginSpawnSelectedCharacterForClient(
        ulong clientId,
        string accountId,
        string characterId)
    {
        if (!_networkManager.IsServer)
        {
            Debug.LogWarning("[ServerTravelManager] BeginSpawnSelectedCharacterForClient called on non-server instance.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(characterId))
        {
            Debug.LogWarning("[ServerTravelManager] Invalid account or character ID.");
            return false;
        }

        if (_spawnRoutinesInProgress.Contains(clientId))
        {
            return false;
        }

        if (!_networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            return false;
        }

        if (client.PlayerObject != null)
        {
            Debug.LogWarning($"[ServerTravelManager] ClientId {clientId} already has a gameplay PlayerObject.");
            return false;
        }

        _spawnRoutinesInProgress.Add(clientId);

        StartCoroutine(SpawnSelectedCharacterRoutine(clientId, accountId, characterId));

        return true;
    }

    private IEnumerator SpawnSelectedCharacterRoutine(
        ulong clientId,
        string accountId,
        string characterId)
    {
        bool spawned = false;

        JsonFileSaveStore saveStore = new JsonFileSaveStore();

        CharacterSaveData savedCharacter = null;
        bool hasSavedCharacter =
            saveStore.TryLoadCharacter(characterId, out savedCharacter) &&
            IsUsableSavedCharacter(savedCharacter);

        if (hasSavedCharacter)
        {
            yield return EnsureServerSceneLoadedRoutine(savedCharacter.SceneId);

            if (ClientStillNeedsGameplayPlayer(clientId))
            {
                spawned = SpawnReturningPlayerForClient(clientId, accountId, characterId, savedCharacter);
            }
        }
        else
        {
            yield return EnsureServerSceneLoadedRoutine(initialDestination.sceneName);

            if (ClientStillNeedsGameplayPlayer(clientId))
            {
                spawned = SpawnNewPlayerForClient(clientId, accountId, characterId, initialDestination);
            }
        }

        _spawnRoutinesInProgress.Remove(clientId);

        if (!spawned)
        {
            Debug.LogWarning($"[ServerTravelManager] Failed to spawn selected character '{characterId}' for ClientId {clientId}.");
        }
    }

    private bool ClientStillNeedsGameplayPlayer(ulong clientId)
    {
        if (!_networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            return false;
        }

        return client.PlayerObject == null;
    }

    private bool SpawnReturningPlayerForClient(
        ulong clientId,
        string accountId,
        string characterId,
        CharacterSaveData savedCharacter)
    {
        Scene savedScene = SceneManager.GetSceneByName(savedCharacter.SceneId);

        if (!savedScene.IsValid() || !savedScene.isLoaded)
        {
            Debug.LogWarning(
                $"[ServerTravelManager] Returning spawn failed: saved scene '{savedCharacter.SceneId}' is not loaded.");
            return false;
        }

        Vector3 spawnPosition = savedCharacter.Position.ToVector3();
        Quaternion spawnRotation = Quaternion.Euler(0f, savedCharacter.Yaw, 0f);

        NetworkObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);

        playerInstance.ActiveSceneSynchronization = false;
        playerInstance.SceneMigrationSynchronization = false;

        PlayerPersistenceRoot persistenceRoot = playerInstance.GetComponent<PlayerPersistenceRoot>();

        if (persistenceRoot != null)
        {
            persistenceRoot.InitializeIdentityServer(accountId, characterId);
        }

        playerInstance.SpawnAsPlayerObject(clientId, destroyPlayerWithScene);

        Debug.Log(
            $"[ServerTravelManager] Spawned returning player for ClientId {clientId} at scene '{savedCharacter.SceneId}', position {spawnPosition}.");

        return true;
    }

    private bool SpawnNewPlayerForClient(
        ulong clientId,
        string accountId,
        string characterId,
        TravelDestination destination)
    {
        if (!TryResolveSpawnPoint(destination, out SpawnPoint spawnPoint))
        {
            Debug.LogError($"[ServerTravelManager] Could not resolve new-character spawn point for destination {destination}.");
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

        PlayerPersistenceRoot persistenceRoot = playerInstance.GetComponent<PlayerPersistenceRoot>();

        if (persistenceRoot != null)
        {
            persistenceRoot.InitializeIdentityServer(accountId, characterId);
        }

        playerInstance.SpawnAsPlayerObject(clientId, destroyPlayerWithScene);

        if (arrivalProfile != null)
        {
            arrivalProfile.ApplyToPlayer(playerInstance.gameObject);
            Debug.Log($"[ServerTravelManager] Applied initial arrival profile for new character on ClientId {clientId}.");
        }

        PlayerAreaStreamingController areaController =
            playerInstance.GetComponent<PlayerAreaStreamingController>();

        if (areaController != null)
        {
            StartCoroutine(ApplyNewCharacterAreaWhenReady(areaController, destination.sceneName));
        }

        Debug.Log($"[ServerTravelManager] Spawned new player for ClientId {clientId} at {destination}.");

        return true;
    }

    private IEnumerator ApplyNewCharacterAreaWhenReady(
        PlayerAreaStreamingController areaController,
        string initialSceneName)
    {
        if (areaController == null || string.IsNullOrWhiteSpace(initialSceneName))
        {
            yield break;
        }

        const int maxFramesToWait = 10;

        for (int i = 0; i < maxFramesToWait; i++)
        {
            if (areaController.HasInitializedAreaState)
            {
                areaController.SetCurrentAreaFromPersistenceServer(initialSceneName, true);
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning(
            $"[ServerTravelManager] Could not apply new-character area '{initialSceneName}' because PlayerAreaStreamingController did not initialize in time.",
            areaController);
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

    private IEnumerator EnsureServerSceneLoadedRoutine(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            yield break;
        }

        Scene existingScene = SceneManager.GetSceneByName(sceneName);

        if (existingScene.IsValid() && existingScene.isLoaded)
        {
            yield break;
        }

        if (verboseLogging)
        {
            Debug.Log($"[ServerTravelManager] Loading server scene '{sceneName}' additively for character spawn.");
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        if (loadOperation == null)
        {
            Debug.LogError($"[ServerTravelManager] Failed to start additive load for scene '{sceneName}'.");
            yield break;
        }

        while (!loadOperation.isDone)
        {
            yield return null;
        }

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);

        if (!loadedScene.IsValid() || !loadedScene.isLoaded)
        {
            Debug.LogError($"[ServerTravelManager] Scene '{sceneName}' did not load successfully.");
        }
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

    private static bool IsUsableSavedCharacter(CharacterSaveData savedCharacter)
    {
        if (savedCharacter == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(savedCharacter.CharacterId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(savedCharacter.SceneId))
        {
            return false;
        }

        return true;
    }
}