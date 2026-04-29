using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Server-side coordinator for manually streamed gameplay area scenes.
///
/// RESPONSIBILITIES:
/// - keep ref-counts for area scenes currently in use by players
/// - load destination scenes on the server when needed
/// - unload server-side area scenes when no players remain in them
/// - resolve spawn points in loaded scenes
/// - coordinate owner-client scene loading through PlayerAreaStreamingController
///
/// IMPORTANT:
/// This manager is intentionally separate from ServerTravelManager.
/// ServerTravelManager still handles the initial synchronized spawn into Intro.
/// This manager handles post-spawn per-player area streaming.
///
/// This version includes the owner-finalization teleport payload, so the owner client
/// is explicitly snapped to the same spawn point position/rotation the server resolved.
/// </summary>
[DisallowMultipleComponent]
public class SceneStreamingManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly Dictionary<string, int> _serverAreaUsageCounts = new();
    private readonly HashSet<string> _serverScenesBeingLoaded = new();

    public void RegisterInitialAreaUsage(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        IncrementServerAreaUsage(sceneName);
    }

    public void UnregisterAreaUsage(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        DecrementServerAreaUsage(sceneName);
    }

    public bool BeginPlayerTransfer(PlayerAreaStreamingController playerController, TravelDestination destination)
    {
        if (playerController == null || !destination.IsValid)
        {
            return false;
        }

        if (playerController.IsServerTransferInProgress)
        {
            if (verboseLogging)
            {
                Debug.LogWarning($"[SceneStreamingManager] Player '{playerController.name}' already has an area transfer in progress.");
            }

            return false;
        }

        StartCoroutine(BeginPlayerTransferRoutine(playerController, destination));
        return true;
    }

    private IEnumerator BeginPlayerTransferRoutine(PlayerAreaStreamingController playerController, TravelDestination destination)
    {
        string previousArea = playerController.CurrentAreaSceneName;

        yield return EnsureServerSceneLoadedRoutine(destination.sceneName);

        Scene destinationScene = SceneManager.GetSceneByName(destination.sceneName);
        if (!destinationScene.IsValid() || !destinationScene.isLoaded)
        {
            if (verboseLogging)
            {
                Debug.LogError($"[SceneStreamingManager] Destination scene '{destination.sceneName}' failed to load on the server.");
            }

            playerController.AbortTransferOnOwner();
            yield break;
        }

        // Reserve the destination area on the server before asking the owner to load it.
        IncrementServerAreaUsage(destination.sceneName);

        playerController.MarkServerTransferPending(destination, previousArea);
        playerController.BeginOwnerSceneLoad(destination, previousArea);

        if (verboseLogging)
        {
            Debug.Log($"[SceneStreamingManager] Began transfer for player '{playerController.name}' to {destination}.");
        }
    }

    /// <summary>
    /// Called by PlayerAreaStreamingController after the owner client has finished
    /// loading the destination scene locally.
    /// </summary>
    public void HandleOwnerLoadedDestination(
        PlayerAreaStreamingController playerController,
        string destinationSceneName,
        string previousAreaSceneName,
        string spawnPointId)
    {
        if (playerController == null)
        {
            return;
        }

        if (!playerController.MatchesPendingTransfer(destinationSceneName, spawnPointId, previousAreaSceneName))
        {
            if (verboseLogging)
            {
                Debug.LogWarning("[SceneStreamingManager] Ignoring owner-loaded callback because it did not match the pending transfer.");
            }

            return;
        }

        TravelDestination destination = new TravelDestination
        {
            sceneName = destinationSceneName,
            spawnPointId = spawnPointId
        };

        if (!TryResolveSpawnPoint(destination, out SpawnPoint spawnPoint))
        {
            if (verboseLogging)
            {
                Debug.LogError($"[SceneStreamingManager] Could not resolve spawn point '{spawnPointId}' in scene '{destinationSceneName}'.");
            }

            // Roll back server-side destination usage.
            DecrementServerAreaUsage(destinationSceneName);
            playerController.ClearPendingTransferServer();
            playerController.AbortTransferOnOwner();
            return;
        }

        Quaternion rotationToUse = spawnPoint.UseRotation
            ? spawnPoint.transform.rotation
            : playerController.transform.rotation;

        // Move the authoritative/server-side player object.
        // Disable CharacterController during teleport so it does not interfere
        // with the transform snap.
        CharacterController characterController = playerController.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        playerController.transform.SetPositionAndRotation(
            spawnPoint.transform.position,
            rotationToUse);

        if (characterController != null)
        {
            characterController.enabled = true;
        }

        playerController.CompleteServerTransfer(destinationSceneName);

        if (!string.IsNullOrWhiteSpace(previousAreaSceneName) &&
            previousAreaSceneName != destinationSceneName)
        {
            DecrementServerAreaUsage(previousAreaSceneName);
        }

        // IMPORTANT:
        // Send the resolved destination pose to the owner so the local player transform
        // is explicitly snapped to the same spawn point after area load completes.
        playerController.FinalizeOwnerTransfer(
            destinationSceneName,
            previousAreaSceneName,
            spawnPoint.transform.position,
            rotationToUse);

        if (verboseLogging)
        {
            Debug.Log($"[SceneStreamingManager] Completed transfer for player '{playerController.name}' to {destination} at {spawnPoint.transform.position}.");
            Debug.Log($"[SceneStreamingManager] Server authoritative player position after teleport: {playerController.transform.position}");
        }
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

        if (_serverScenesBeingLoaded.Contains(sceneName))
        {
            while (_serverScenesBeingLoaded.Contains(sceneName))
            {
                yield return null;
            }

            yield break;
        }

        _serverScenesBeingLoaded.Add(sceneName);

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (loadOperation == null)
        {
            _serverScenesBeingLoaded.Remove(sceneName);
            yield break;
        }

        while (!loadOperation.isDone)
        {
            yield return null;
        }

        _serverScenesBeingLoaded.Remove(sceneName);

        if (verboseLogging)
        {
            Debug.Log($"[SceneStreamingManager] Loaded server area scene '{sceneName}'.");
        }
    }

    private void IncrementServerAreaUsage(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        if (_serverAreaUsageCounts.TryGetValue(sceneName, out int currentCount))
        {
            _serverAreaUsageCounts[sceneName] = currentCount + 1;
        }
        else
        {
            _serverAreaUsageCounts[sceneName] = 1;
        }

        if (verboseLogging)
        {
            Debug.Log($"[SceneStreamingManager] Area '{sceneName}' usage count is now {_serverAreaUsageCounts[sceneName]}.");
        }
    }

    private void DecrementServerAreaUsage(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        if (!_serverAreaUsageCounts.TryGetValue(sceneName, out int currentCount))
        {
            return;
        }

        currentCount--;
        if (currentCount > 0)
        {
            _serverAreaUsageCounts[sceneName] = currentCount;

            if (verboseLogging)
            {
                Debug.Log($"[SceneStreamingManager] Area '{sceneName}' usage count is now {currentCount}.");
            }

            return;
        }

        _serverAreaUsageCounts.Remove(sceneName);

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid() && loadedScene.isLoaded)
        {
            StartCoroutine(UnloadServerSceneRoutine(sceneName));
        }
    }

    private IEnumerator UnloadServerSceneRoutine(string sceneName)
    {
        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (!loadedScene.IsValid() || !loadedScene.isLoaded)
        {
            yield break;
        }

        AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(sceneName);
        if (unloadOperation == null)
        {
            yield break;
        }

        while (!unloadOperation.isDone)
        {
            yield return null;
        }

        if (verboseLogging)
        {
            Debug.Log($"[SceneStreamingManager] Unloaded server area scene '{sceneName}' because no players remained in it.");
        }
    }

    public bool TryResolveSpawnPoint(TravelDestination destination, out SpawnPoint spawnPoint)
    {
        spawnPoint = null;

        if (string.IsNullOrWhiteSpace(destination.sceneName) ||
            string.IsNullOrWhiteSpace(destination.spawnPointId))
        {
            return false;
        }

        Scene scene = SceneManager.GetSceneByName(destination.sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        GameObject[] rootObjects = scene.GetRootGameObjects();

        for (int i = 0; i < rootObjects.Length; i++)
        {
            SpawnPoint[] spawnPoints = rootObjects[i].GetComponentsInChildren<SpawnPoint>(true);

            for (int j = 0; j < spawnPoints.Length; j++)
            {
                if (spawnPoints[j].SpawnPointId == destination.spawnPointId)
                {
                    spawnPoint = spawnPoints[j];
                    return true;
                }
            }
        }

        return false;
    }
}