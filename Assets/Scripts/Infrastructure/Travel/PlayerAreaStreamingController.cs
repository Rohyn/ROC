using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// Per-player area streaming controller.
///
/// DESIGN:
/// - Lives on the player prefab
/// - Makes the player object persistent across streamed scene unloads
/// - Coordinates owner-only area scene loading
/// - Lets the server keep an authoritative "current area scene" per player
///
/// IMPORTANT:
/// This batch keeps the player object itself out of the streamed scenes by moving it
/// into DontDestroyOnLoad on each peer. This avoids relying on automatic scene migration
/// for the player object during custom/manual area streaming.
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
    private bool _serverTransferInProgress;
    private bool _ownerTransferInProgress;
    private TravelDestination _pendingDestination;
    private string _pendingPreviousAreaSceneName;

    public string CurrentAreaSceneName => _currentAreaSceneName;
    public bool IsServerTransferInProgress => _serverTransferInProgress;

    /// <summary>
    /// Local convenience for UI/interaction suppression.
    /// Returns true if a transfer is currently in progress on this instance.
    /// </summary>
    public bool IsAreaTransferInProgress => _serverTransferInProgress || _ownerTransferInProgress;

    public override void OnNetworkSpawn()
    {
        // Keep the player object persistent across streamed scene unloads.
        DontDestroyOnLoad(gameObject);

        // We are not relying on automatic scene migration for this streamed-area model.
        NetworkObject.ActiveSceneSynchronization = false;
        NetworkObject.SceneMigrationSynchronization = false;

        _currentAreaSceneName = initialAreaSceneName;

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

    /// <summary>
    /// Server-only entry point used by streamed area transfer actions.
    /// </summary>
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
        return _serverTransferInProgress &&
               _pendingDestination.sceneName == destinationSceneName &&
               _pendingDestination.spawnPointId == spawnPointId &&
               _pendingPreviousAreaSceneName == previousAreaSceneName;
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

    public void FinalizeOwnerTransfer(string newAreaSceneName, string previousAreaSceneName)
    {
        FinalizeOwnerTransferRpc(newAreaSceneName, previousAreaSceneName, unloadPreviousAreaOnOwner);
    }

    public void AbortTransferOnOwner()
    {
        AbortOwnerTransferRpc();
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
            spawnPointId,
            shouldUnloadPreviousArea));
    }

    private IEnumerator LoadAreaSceneForOwnerRoutine(
        string destinationSceneName,
        string previousAreaSceneName,
        string spawnPointId,
        bool shouldUnloadPreviousArea)
    {
        if (!string.IsNullOrWhiteSpace(destinationSceneName))
        {
            Scene destinationScene = SceneManager.GetSceneByName(destinationSceneName);

            if (!destinationScene.IsValid() || !destinationScene.isLoaded)
            {
                AsyncOperation loadOperation =
                    SceneManager.LoadSceneAsync(destinationSceneName, LoadSceneMode.Additive);

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
        bool shouldUnloadPreviousArea)
    {
        StartCoroutine(FinalizeOwnerTransferRoutine(
            newAreaSceneName,
            previousAreaSceneName,
            shouldUnloadPreviousArea));
    }

    private IEnumerator FinalizeOwnerTransferRoutine(
        string newAreaSceneName,
        string previousAreaSceneName,
        bool shouldUnloadPreviousArea)
    {
        _currentAreaSceneName = newAreaSceneName;

        Scene newScene = SceneManager.GetSceneByName(newAreaSceneName);
        if (newScene.IsValid() && newScene.isLoaded)
        {
            SceneManager.SetActiveScene(newScene);
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

        _ownerTransferInProgress = false;

        if (verboseLogging)
        {
            Debug.Log($"[PlayerAreaStreamingController] Owner finalized area transfer into '{newAreaSceneName}'.", this);
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