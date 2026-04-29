using System.Collections;
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
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerAreaStreamingController : NetworkBehaviour
{
    [Header("Initial Area")]
    [Tooltip("The area scene the player begins in after the initial synchronized spawn.")]
    [SerializeField] private string initialAreaSceneName = "Intro";

    [Header("Client Unload Behavior")]
    [Tooltip("If true, the owner client unloads the previous area scene after the new one is ready.")]
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
        if (!string.IsNullOrWhiteSpace(areaSceneName))
        {
            Scene destinationScene = SceneManager.GetSceneByName(areaSceneName);

            if (!destinationScene.IsValid() || !destinationScene.isLoaded)
            {
                AsyncOperation loadOperation = SceneManager.LoadSceneAsync(areaSceneName, LoadSceneMode.Additive);

                if (loadOperation != null)
                {
                    while (!loadOperation.isDone)
                    {
                        yield return null;
                    }
                }
            }

            Scene loadedDestinationScene = SceneManager.GetSceneByName(areaSceneName);

            if (loadedDestinationScene.IsValid() && loadedDestinationScene.isLoaded)
            {
                SceneManager.SetActiveScene(loadedDestinationScene);
            }
        }

        _currentAreaSceneName = areaSceneName;

        if (shouldUnloadPreviousArea &&
            !string.IsNullOrWhiteSpace(previousAreaSceneName) &&
            previousAreaSceneName != areaSceneName)
        {
            Scene previousScene = SceneManager.GetSceneByName(previousAreaSceneName);

            if (previousScene.IsValid() && previousScene.isLoaded)
            {
                AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(previousAreaSceneName);

                if (unloadOperation != null)
                {
                    while (!unloadOperation.isDone)
                    {
                        yield return null;
                    }
                }
            }
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner restored persisted area '{areaSceneName}'. Previous='{previousAreaSceneName}'.",
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
        if (!string.IsNullOrWhiteSpace(destinationSceneName))
        {
            Scene destinationScene = SceneManager.GetSceneByName(destinationSceneName);

            if (!destinationScene.IsValid() || !destinationScene.isLoaded)
            {
                AsyncOperation loadOperation = SceneManager.LoadSceneAsync(destinationSceneName, LoadSceneMode.Additive);

                if (loadOperation != null)
                {
                    while (!loadOperation.isDone)
                    {
                        yield return null;
                    }
                }
            }

            Scene loadedDestinationScene = SceneManager.GetSceneByName(destinationSceneName);

            if (loadedDestinationScene.IsValid() && loadedDestinationScene.isLoaded)
            {
                SceneManager.SetActiveScene(loadedDestinationScene);
            }
        }

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

        Scene newScene = SceneManager.GetSceneByName(newAreaSceneName);

        if (newScene.IsValid() && newScene.isLoaded)
        {
            SceneManager.SetActiveScene(newScene);
        }

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

        if (shouldUnloadPreviousArea &&
            !string.IsNullOrWhiteSpace(previousAreaSceneName) &&
            previousAreaSceneName != newAreaSceneName)
        {
            Scene previousScene = SceneManager.GetSceneByName(previousAreaSceneName);

            if (previousScene.IsValid() && previousScene.isLoaded)
            {
                AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(previousAreaSceneName);

                if (unloadOperation != null)
                {
                    while (!unloadOperation.isDone)
                    {
                        yield return null;
                    }
                }
            }
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[PlayerAreaStreamingController] Owner local player position after previous scene unload: {transform.position}",
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
}