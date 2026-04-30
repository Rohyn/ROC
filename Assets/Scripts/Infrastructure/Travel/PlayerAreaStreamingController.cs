using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Per-player area streaming controller.
///
/// DESIGN:
/// - Lives on the player prefab.
/// - Makes the player object persistent across streamed scene unloads.
/// - Coordinates owner-only area scene loading.
/// - Lets the server keep an authoritative "current area scene" per player.
///
/// IMPORTANT:
/// The player object itself should stay persistent.
/// Do not move the player GameObject into additive area scenes.
/// Area scene membership is tracked logically through CurrentAreaSceneName.
///
/// OWNER CLEANUP:
/// On reconnect, a client may synchronize/load multiple gameplay area scenes
/// if the server currently has them loaded. Whenever this owner restores/finalizes
/// its current area, unload all other currently loaded gameplay area scenes.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerAreaStreamingController : NetworkBehaviour
{
    [Header("Initial Area")]
    [Tooltip("The area scene the player begins in after the initial synchronized spawn.")]
    [SerializeField] private string initialAreaSceneName = "Area_Intro";

    [Header("Owner Area Scene Cleanup")]
    [Tooltip("If true, the owner client unloads other loaded gameplay area scenes after the desired area is ready.")]
    [SerializeField] private bool cleanupOtherAreaScenesOnOwner = true;

    [Tooltip("Loaded scenes whose names start with this prefix are considered gameplay area scenes.")]
    [SerializeField] private string gameplayAreaSceneNamePrefix = "Area_";

    [Tooltip("Optional explicit gameplay area scene names. Useful for legacy scenes or exceptions.")]
    [SerializeField] private string[] explicitGameplayAreaSceneNames = new string[0];

    [Header("Client Unload Behavior")]
    [Tooltip("If true, the owner client also attempts to unload the previous area scene during normal transfers.")]
    [SerializeField] private bool unloadPreviousAreaOnOwner = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private string _currentAreaSceneName;
    private bool _hasInitializedAreaState;
    private bool _serverTransferInProgress;
    private bool _ownerTransferInProgress;

    private TravelDestination _pendingDestination;
    private string _pendingPreviousAreaSceneName;

    public string CurrentAreaSceneName => _currentAreaSceneName;
    public bool HasInitializedAreaState => _hasInitializedAreaState;
    public bool IsServerTransferInProgress => _serverTransferInProgress;
    public bool IsAreaTransferInProgress => _serverTransferInProgress || _ownerTransferInProgress;

    public override void OnNetworkSpawn()
    {
        DontDestroyOnLoad(gameObject);

        NetworkObject.ActiveSceneSynchronization = false;
        NetworkObject.SceneMigrationSynchronization = false;

        _currentAreaSceneName = initialAreaSceneName;
        _hasInitializedAreaState = true;

        if (IsServer)
        {
            SceneStreamingManager manager = FindFirstObjectByType<SceneStreamingManager>();
            if (manager != null)
            {
                manager.RegisterInitialAreaUsage(_currentAreaSceneName);
            }
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerAreaStreamingController] Initialized with current area '{_currentAreaSceneName}'.", this);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            SceneStreamingManager manager = FindFirstObjectByType<SceneStreamingManager>();

            if (manager != null && !string.IsNullOrWhiteSpace(_currentAreaSceneName))
            {
                manager.UnregisterAreaUsage(_currentAreaSceneName);
            }
        }
    }

    public void SetCurrentAreaFromPersistenceServer(string areaSceneName, bool loadAreaOnOwner = true)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerAreaStreamingController] SetCurrentAreaFromPersistenceServer called on non-server instance.", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(areaSceneName))
        {
            return;
        }

        string previousArea = _currentAreaSceneName;

        if (previousArea == areaSceneName)
        {
            if (loadAreaOnOwner)
            {
                RestoreAreaForOwnerRpc(areaSceneName, previousArea, false);
            }

            return;
        }

        SceneStreamingManager manager = FindFirstObjectByType<SceneStreamingManager>();

        if (manager != null)
        {
            if (!string.IsNullOrWhiteSpace(previousArea))
            {
                manager.UnregisterAreaUsage(previousArea);
            }

            manager.RegisterInitialAreaUsage(areaSceneName);
        }

        _currentAreaSceneName = areaSceneName;

        if (loadAreaOnOwner)
        {
            RestoreAreaForOwnerRpc(areaSceneName, previousArea, unloadPreviousAreaOnOwner);
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Current area restored from persistence. Previous='{previousArea}', Current='{_currentAreaSceneName}'.",
                this);
        }
    }

    public bool BeginAreaTransfer(TravelDestination destination)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerAreaStreamingController] BeginAreaTransfer called on non-server instance.", this);
            return false;
        }

        SceneStreamingManager manager = FindFirstObjectByType<SceneStreamingManager>();

        if (manager == null)
        {
            Debug.LogWarning("[PlayerAreaStreamingController] No SceneStreamingManager found.", this);
            return false;
        }

        return manager.BeginPlayerTransfer(this, destination);
    }

    public void MarkServerTransferPending(TravelDestination destination, string previousAreaSceneName)
    {
        _serverTransferInProgress = true;
        _pendingDestination = destination;
        _pendingPreviousAreaSceneName = previousAreaSceneName;
    }

    public bool MatchesPendingTransfer(string destinationSceneName, string spawnPointId, string previousAreaSceneName)
    {
        return _serverTransferInProgress
               && _pendingDestination.sceneName == destinationSceneName
               && _pendingDestination.spawnPointId == spawnPointId
               && _pendingPreviousAreaSceneName == previousAreaSceneName;
    }

    public void ClearPendingTransferServer()
    {
        _serverTransferInProgress = false;
        _pendingDestination = default;
        _pendingPreviousAreaSceneName = string.Empty;
    }

    public void CompleteServerTransfer(string newAreaSceneName)
    {
        _currentAreaSceneName = newAreaSceneName;
        ClearPendingTransferServer();
    }

    public void BeginOwnerSceneLoad(TravelDestination destination, string previousAreaSceneName)
    {
        LoadAreaSceneForOwnerRpc(
            destination.sceneName,
            previousAreaSceneName,
            destination.spawnPointId,
            unloadPreviousAreaOnOwner);
    }

    public void FinalizeOwnerTransfer(
        string newAreaSceneName,
        string previousAreaSceneName,
        Vector3 destinationPosition,
        Quaternion destinationRotation)
    {
        FinalizeOwnerTransferRpc(
            newAreaSceneName,
            previousAreaSceneName,
            unloadPreviousAreaOnOwner,
            destinationPosition,
            destinationRotation);
    }

    public void AbortTransferOnOwner()
    {
        AbortOwnerTransferRpc();
    }

    [Rpc(SendTo.Owner)]
    private void RestoreAreaForOwnerRpc(
        string areaSceneName,
        string previousAreaSceneName,
        bool shouldUnloadPreviousArea)
    {
        StartCoroutine(RestoreAreaForOwnerRoutine(areaSceneName, previousAreaSceneName, shouldUnloadPreviousArea));
    }

    private IEnumerator RestoreAreaForOwnerRoutine(
        string areaSceneName,
        string previousAreaSceneName,
        bool shouldUnloadPreviousArea)
    {
        yield return EnsureOwnerAreaSceneLoadedAndActive(areaSceneName);

        _currentAreaSceneName = areaSceneName;

        if (shouldUnloadPreviousArea)
        {
            yield return UnloadOwnerSceneIfLoaded(previousAreaSceneName, areaSceneName);
        }

        if (cleanupOtherAreaScenesOnOwner)
        {
            yield return CleanupOtherLoadedAreaScenes(areaSceneName);
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner restored area '{areaSceneName}'. Previous='{previousAreaSceneName}'.",
                this);
        }
    }

    [Rpc(SendTo.Owner)]
    private void LoadAreaSceneForOwnerRpc(
        string destinationSceneName,
        string previousAreaSceneName,
        string spawnPointId,
        bool shouldUnloadPreviousArea)
    {
        _ownerTransferInProgress = true;

        StartCoroutine(LoadAreaSceneForOwnerRoutine(
            destinationSceneName,
            previousAreaSceneName,
            spawnPointId));
    }

    private IEnumerator LoadAreaSceneForOwnerRoutine(
        string destinationSceneName,
        string previousAreaSceneName,
        string spawnPointId)
    {
        yield return EnsureOwnerAreaSceneLoadedAndActive(destinationSceneName);

        if (verboseLogging)
        {
            Debug.Log($"[PlayerAreaStreamingController] Owner finished loading area scene '{destinationSceneName}'.", this);
        }

        OwnerFinishedLoadingAreaRpc(destinationSceneName, previousAreaSceneName, spawnPointId);
    }

    [Rpc(SendTo.Server)]
    private void OwnerFinishedLoadingAreaRpc(
        string destinationSceneName,
        string previousAreaSceneName,
        string spawnPointId)
    {
        SceneStreamingManager manager = FindFirstObjectByType<SceneStreamingManager>();

        if (manager != null)
        {
            manager.HandleOwnerLoadedDestination(
                this,
                destinationSceneName,
                previousAreaSceneName,
                spawnPointId);
        }
    }

    [Rpc(SendTo.Owner)]
    private void FinalizeOwnerTransferRpc(
        string newAreaSceneName,
        string previousAreaSceneName,
        bool shouldUnloadPreviousArea,
        Vector3 destinationPosition,
        Quaternion destinationRotation)
    {
        StartCoroutine(FinalizeOwnerTransferRoutine(
            newAreaSceneName,
            previousAreaSceneName,
            shouldUnloadPreviousArea,
            destinationPosition,
            destinationRotation));
    }

    private IEnumerator FinalizeOwnerTransferRoutine(
        string newAreaSceneName,
        string previousAreaSceneName,
        bool shouldUnloadPreviousArea,
        Vector3 destinationPosition,
        Quaternion destinationRotation)
    {
        _currentAreaSceneName = newAreaSceneName;

        yield return EnsureOwnerAreaSceneLoadedAndActive(newAreaSceneName);

        CharacterController characterController = GetComponent<CharacterController>();

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(destinationPosition, destinationRotation);

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner local player position immediately after teleport: {transform.position}",
                this);
        }

        if (characterController != null)
        {
            characterController.enabled = true;
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner local player position after CharacterController re-enable: {transform.position}",
                this);
        }

        if (shouldUnloadPreviousArea)
        {
            yield return UnloadOwnerSceneIfLoaded(previousAreaSceneName, newAreaSceneName);
        }

        if (cleanupOtherAreaScenesOnOwner)
        {
            yield return CleanupOtherLoadedAreaScenes(newAreaSceneName);
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner local player position after area cleanup: {transform.position}",
                this);
        }

        _ownerTransferInProgress = false;

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner finalized area transfer into '{newAreaSceneName}' at {destinationPosition}.",
                this);
        }
    }

    [Rpc(SendTo.Owner)]
    private void AbortOwnerTransferRpc()
    {
        _ownerTransferInProgress = false;

        if (verboseLogging)
        {
            Debug.Log("[PlayerAreaStreamingController] Owner transfer aborted.", this);
        }
    }

    private IEnumerator EnsureOwnerAreaSceneLoadedAndActive(string areaSceneName)
    {
        if (string.IsNullOrWhiteSpace(areaSceneName))
        {
            yield break;
        }

        Scene destinationScene = SceneManager.GetSceneByName(areaSceneName);

        if (!destinationScene.IsValid() || !destinationScene.isLoaded)
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(areaSceneName, LoadSceneMode.Additive);

            if (loadOperation == null)
            {
                Debug.LogError($"[PlayerAreaStreamingController] Failed to start loading area scene '{areaSceneName}'.", this);
                yield break;
            }

            while (!loadOperation.isDone)
            {
                yield return null;
            }
        }

        Scene loadedScene = SceneManager.GetSceneByName(areaSceneName);

        if (loadedScene.IsValid() && loadedScene.isLoaded)
        {
            SceneManager.SetActiveScene(loadedScene);
        }
        else
        {
            Debug.LogWarning($"[PlayerAreaStreamingController] Area scene '{areaSceneName}' was not loaded successfully.", this);
        }
    }

    private IEnumerator CleanupOtherLoadedAreaScenes(string keepAreaSceneName)
    {
        if (string.IsNullOrWhiteSpace(keepAreaSceneName))
        {
            yield break;
        }

        List<Scene> scenesToUnload = new List<Scene>();

        int loadedSceneCount = SceneManager.sceneCount;

        for (int i = 0; i < loadedSceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.IsValid() || !scene.isLoaded)
            {
                continue;
            }

            if (scene.name == keepAreaSceneName)
            {
                continue;
            }

            if (!IsGameplayAreaScene(scene))
            {
                continue;
            }

            scenesToUnload.Add(scene);
        }

        for (int i = 0; i < scenesToUnload.Count; i++)
        {
            Scene scene = scenesToUnload[i];

            if (!scene.IsValid() || !scene.isLoaded)
            {
                continue;
            }

            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(scene);

            if (unloadOperation == null)
            {
                continue;
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"[PlayerAreaStreamingController] Owner unloading non-current area scene '{scene.name}'. Keeping '{keepAreaSceneName}'.",
                    this);
            }

            while (!unloadOperation.isDone)
            {
                yield return null;
            }
        }
    }

    private IEnumerator UnloadOwnerSceneIfLoaded(string sceneName, string keepSceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(keepSceneName) && sceneName == keepSceneName)
        {
            yield break;
        }

        Scene scene = SceneManager.GetSceneByName(sceneName);

        if (!scene.IsValid() || !scene.isLoaded)
        {
            yield break;
        }

        AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(scene);

        if (unloadOperation == null)
        {
            yield break;
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner unloading scene '{sceneName}'. Keeping '{keepSceneName}'.",
                this);
        }

        while (!unloadOperation.isDone)
        {
            yield return null;
        }
    }

    private bool IsGameplayAreaScene(Scene scene)
    {
        if (!scene.IsValid() || string.IsNullOrWhiteSpace(scene.name))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(gameplayAreaSceneNamePrefix) &&
            scene.name.StartsWith(gameplayAreaSceneNamePrefix, System.StringComparison.Ordinal))
        {
            return true;
        }

        if (explicitGameplayAreaSceneNames != null)
        {
            for (int i = 0; i < explicitGameplayAreaSceneNames.Length; i++)
            {
                string explicitSceneName = explicitGameplayAreaSceneNames[i];

                if (string.IsNullOrWhiteSpace(explicitSceneName))
                {
                    continue;
                }

                if (scene.name == explicitSceneName)
                {
                    return true;
                }
            }
        }

        return false;
    }
}