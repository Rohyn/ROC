using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Generic travel trigger / portal destination holder.
///
/// This does not implement your full interaction system yet.
/// It is just the correct destination abstraction for later use.
///
/// Later, your interaction system can call into this component on the server
/// when the player uses a door, ladder, portal, recall stone, etc.
/// </summary>
public class TravelPortal : MonoBehaviour
{
    [Header("Destination")]
    [SerializeField] private TravelDestination destination;

    public TravelDestination Destination => destination;

    /// <summary>
    /// Example server-side activation entry point.
    /// In a real game, this would likely be called by an interactable system,
    /// not directly by random clients.
    /// </summary>
    public bool TryUse(NetworkObject playerObject)
    {
        if (playerObject == null || !NetworkManager.Singleton.IsServer)
        {
            return false;
        }

        ServerTravelManager travelManager = FindFirstObjectByType<ServerTravelManager>();
        if (travelManager == null)
        {
            Debug.LogError("[TravelPortal] No ServerTravelManager found.");
            return false;
        }

        // If the destination is in the current scene, do a same-scene teleport.
        if (playerObject.gameObject.scene.name == destination.sceneName)
        {
            return travelManager.TeleportPlayerInCurrentScene(playerObject, destination.spawnPointId);
        }

        // Otherwise, attempt cross-scene transfer.
        return travelManager.TransferPlayerToLoadedScene(playerObject, destination);
    }
}