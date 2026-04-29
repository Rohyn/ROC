using System.Collections.Generic;
using ROC.Session;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class ServerCharacterSessionManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkObject playerSessionPrefab;
    [SerializeField] private ServerTravelManager serverTravelManager;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private NetworkManager _networkManager;
    private bool _sceneEventsSubscribed;

    private readonly Dictionary<ulong, NetworkObject> _sessionsByClientId = new();

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();

        if (serverTravelManager == null)
        {
            serverTravelManager = GetComponent<ServerTravelManager>();
        }

        if (_networkManager == null)
        {
            Debug.LogError("[ServerCharacterSessionManager] Missing NetworkManager.");
            enabled = false;
            return;
        }

        if (playerSessionPrefab == null)
        {
            Debug.LogError("[ServerCharacterSessionManager] PlayerSession prefab is not assigned.");
            enabled = false;
            return;
        }

        if (serverTravelManager == null)
        {
            Debug.LogError("[ServerCharacterSessionManager] ServerTravelManager is not assigned.");
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
        _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDisable()
    {
        if (_networkManager == null)
        {
            return;
        }

        _networkManager.OnServerStarted -= HandleServerStarted;
        _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

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

        if (_networkManager.SceneManager == null)
        {
            Debug.LogError("[ServerCharacterSessionManager] NetworkManager.SceneManager is null.");
            return;
        }

        if (!_sceneEventsSubscribed)
        {
            _networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
            _sceneEventsSubscribed = true;

            if (verboseLogging)
            {
                Debug.Log("[ServerCharacterSessionManager] Subscribed to NetworkSceneManager.OnSceneEvent.");
            }
        }
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        if (sceneEvent.SceneEventType != SceneEventType.SynchronizeComplete)
        {
            return;
        }

        ulong clientId = sceneEvent.ClientId;

        if (clientId == NetworkManager.ServerClientId)
        {
            return;
        }

        SpawnSessionForClient(clientId);
    }

    private void SpawnSessionForClient(ulong clientId)
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        if (_sessionsByClientId.ContainsKey(clientId))
        {
            return;
        }

        if (!_networkManager.ConnectedClients.ContainsKey(clientId))
        {
            return;
        }

        NetworkObject sessionInstance = Instantiate(playerSessionPrefab);

        sessionInstance.ActiveSceneSynchronization = false;
        sessionInstance.SceneMigrationSynchronization = false;

        sessionInstance.SpawnWithOwnership(clientId, destroyWithScene: false);

        _sessionsByClientId[clientId] = sessionInstance;

        if (verboseLogging)
        {
            Debug.Log($"[ServerCharacterSessionManager] Spawned PlayerSession for ClientId {clientId}.");
        }
    }

    public void HandleContinueRequested(PlayerSession session)
    {
        if (!_networkManager.IsServer)
        {
            return;
        }

        if (session == null)
        {
            return;
        }

        ulong clientId = session.OwnerClientId;

        if (session.IsSelectionLockedServer)
        {
            return;
        }

        if (!_networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            session.NotifyCharacterSelectionFailedOwnerRpc("Client is no longer connected.");
            return;
        }

        if (client.PlayerObject != null)
        {
            session.NotifyCharacterSelectionFailedOwnerRpc("Gameplay character is already spawned.");
            return;
        }

        session.MarkSelectionLockedServer();

        bool spawnStarted = serverTravelManager.BeginSpawnDefaultCharacterForClient(clientId);

        if (!spawnStarted)
        {
            session.UnlockSelectionServer();
            session.NotifyCharacterSelectionFailedOwnerRpc("Server could not start character spawn.");
            return;
        }

        session.NotifyCharacterSelectionAcceptedOwnerRpc();

        if (verboseLogging)
        {
            Debug.Log($"[ServerCharacterSessionManager] Continue accepted for ClientId {clientId}.");
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (_sessionsByClientId.TryGetValue(clientId, out NetworkObject sessionObject) && sessionObject != null)
        {
            if (sessionObject.IsSpawned)
            {
                sessionObject.Despawn(true);
            }
            else
            {
                Destroy(sessionObject.gameObject);
            }
        }

        _sessionsByClientId.Remove(clientId);
    }
}