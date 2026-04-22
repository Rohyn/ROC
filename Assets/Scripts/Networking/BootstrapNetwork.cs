using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Very early network bootstrap for Ruins of Crestil.
///
/// Attach this to the same GameObject as:
/// - NetworkManager
/// - UnityTransport
///
/// Intended behavior:
/// - In a dedicated server build, automatically start the server.
/// - In a client build, do NOT start anything automatically.
/// - Let some other system (such as AppFlowController / main menu UI)
///   decide when to call StartClient().
///
/// This keeps networking separate from menu / branding / splash flow.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
public class BootstrapNetwork : MonoBehaviour
{
    [Header("Assigned Config")]
    [Tooltip("Runtime network settings such as address, port, and max players.")]
    [SerializeField] private NetworkRuntimeConfig runtimeConfig;

    [Header("Server Startup")]
    [Tooltip("If true, dedicated server builds will auto-start the server on launch.")]
    [SerializeField] private bool autoStartDedicatedServer = true;

    // Cached references so we only fetch components once.
    private NetworkManager _networkManager;
    private UnityTransport _transport;

    // Guard flag to prevent duplicate shutdown logic.
    private bool _hasShutdown;

    /// <summary>
    /// Cache references and validate setup as early as possible.
    /// </summary>
    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        _transport = GetComponent<UnityTransport>();

        if (_networkManager == null)
        {
            Debug.LogError("[BootstrapNetwork] NetworkManager component is missing.");
            enabled = false;
            return;
        }

        if (_transport == null)
        {
            Debug.LogError("[BootstrapNetwork] UnityTransport component is missing.");
            enabled = false;
            return;
        }

        if (runtimeConfig == null)
        {
            Debug.LogError("[BootstrapNetwork] NetworkRuntimeConfig is not assigned.");
            enabled = false;
            return;
        }

        // We do NOT call DontDestroyOnLoad here.
        // The AppRoot / flow-controller side should decide how the application
        // persists across scenes. If this BootstrapNetwork lives on AppRoot,
        // and AppRoot is marked DontDestroyOnLoad elsewhere, that is enough.
    }

    /// <summary>
    /// Apply network configuration and, if this is a dedicated server build,
    /// optionally auto-start the server.
    /// </summary>
    private void Start()
    {
        // Always configure the transport first so its address/port values are ready.
        ConfigureTransport();

        // Always register connection approval before the network starts listening.
        ConfigureConnectionApproval();

#if UNITY_SERVER
        // Dedicated server builds should start automatically.
        if (autoStartDedicatedServer)
        {
            StartDedicatedServer();
        }
#else
        // IMPORTANT:
        // Client builds intentionally do nothing here.
        //
        // We do NOT automatically start as a client.
        // We do NOT automatically start as host.
        //
        // The menu / app flow should explicitly call StartClient()
        // only after the player clicks Play.
#endif
    }

    /// <summary>
    /// Applies transport settings from the runtime config.
    ///
    /// IMPORTANT:
    /// In your installed version of Unity Transport, SetConnectionData expects:
    ///   (bool forceOverrideCommandLineArgs, string ipAddress, ushort port, string listenAddress)
    ///
    /// That is why the bool comes first.
    /// </summary>
    private void ConfigureTransport()
    {
        _transport.SetConnectionData(
            true,
            runtimeConfig.serverAddress,
            runtimeConfig.port,
            runtimeConfig.listenAddress);
    }

    /// <summary>
    /// Enables NGO connection approval and assigns a single approval callback.
    ///
    /// Current NGO versions use direct assignment here, not += subscription.
    /// </summary>
    private void ConfigureConnectionApproval()
    {
        _networkManager.NetworkConfig.ConnectionApproval = true;
        _networkManager.ConnectionApprovalCallback = ApprovalCheck;
    }

    /// <summary>
    /// Starts this process as a dedicated server.
    ///
    /// This should be used by the dedicated server build.
    /// </summary>
    public bool StartDedicatedServer()
    {
        if (_networkManager.IsListening)
        {
            Debug.LogWarning("[BootstrapNetwork] Cannot start server because the NetworkManager is already listening.");
            return false;
        }

        // Re-apply transport settings in case something changed before startup.
        ConfigureTransport();

        bool started = _networkManager.StartServer();

        if (!started)
        {
            Debug.LogWarning("[BootstrapNetwork] Failed to start dedicated server.");
            return false;
        }

        Debug.Log($"[BootstrapNetwork] Dedicated server started on {runtimeConfig.listenAddress}:{runtimeConfig.port}");
        return true;
    }

    /// <summary>
    /// Starts this process as a client using the default config values.
    ///
    /// This is what your menu / flow controller should call when the player
    /// presses the Play button.
    /// </summary>
    public bool StartClient()
    {
        return StartClient(runtimeConfig.serverAddress, runtimeConfig.port);
    }

    /// <summary>
    /// Starts this process as a client using the provided address and port.
    ///
    /// This overload is useful later if you add a server-address input field
    /// to your main menu.
    /// </summary>
    public bool StartClient(string address, ushort port)
    {
        if (_networkManager.IsListening)
        {
            Debug.LogWarning("[BootstrapNetwork] Cannot start client because the NetworkManager is already listening.");
            return false;
        }

        // Update the transport to use the requested connection target.
        _transport.SetConnectionData(
            true,
            address,
            port,
            runtimeConfig.listenAddress);

        bool started = _networkManager.StartClient();

        if (!started)
        {
            Debug.LogWarning("[BootstrapNetwork] Failed to start client.");
            return false;
        }

        Debug.Log($"[BootstrapNetwork] Client attempting connection to {address}:{port}");
        return true;
    }

    /// <summary>
    /// Optional local helper for quick in-editor testing.
    ///
    /// This is NOT your final MMO deployment model.
    /// It is simply handy for fast early development.
    /// </summary>
    public bool StartHostForTesting()
    {
        if (_networkManager.IsListening)
        {
            Debug.LogWarning("[BootstrapNetwork] Cannot start host because the NetworkManager is already listening.");
            return false;
        }

        ConfigureTransport();

        bool started = _networkManager.StartHost();

        if (!started)
        {
            Debug.LogWarning("[BootstrapNetwork] Failed to start host.");
            return false;
        }

        Debug.Log($"[BootstrapNetwork] Host started on port {runtimeConfig.port}");
        return true;
    }

    /// <summary>
    /// Very minimal connection approval.
    ///
    /// For now, this only checks:
    /// - whether the server has room
    ///
    /// Later, this can be expanded to handle:
    /// - authentication / session tickets
    /// - character selection
    /// - whitelist / playtest gating
    /// - shard or world assignment
    /// - tutorial instance placement
    /// </summary>
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        // Start from a safe "deny by default" state.
        response.Approved = false;
        response.CreatePlayerObject = false;
        response.PlayerPrefabHash = null;
        response.Position = Vector3.zero;
        response.Rotation = Quaternion.identity;
        response.Pending = false;
        response.Reason = string.Empty;

        // Simple capacity check.
        if (_networkManager.ConnectedClientsIds.Count >= runtimeConfig.maxPlayers)
        {
            response.Reason = "Server is full.";
            Debug.LogWarning("[BootstrapNetwork] Connection denied because the server is full.");
            return;
        }

        // Approve the client.
        response.Approved = true;

		// IMPORTANT:
		// We are taking manual control of player spawning.
		// NGO will approve the client connection, but it will NOT auto-create
		// the default player prefab during approval.
		response.CreatePlayerObject = false;

		// Since we are not auto-spawning a player prefab here, leave this null.
		response.PlayerPrefabHash = null;

		// These values are ignored when CreatePlayerObject is false,
		// but leaving them set is harmless and explicit.
		response.Position = Vector3.zero;
		response.Rotation = Quaternion.identity;

        Debug.Log($"[BootstrapNetwork] Approved client connection for ClientId {request.ClientNetworkId}.");
    }

    /// <summary>
    /// Clean up when the application quits.
    /// </summary>
    private void OnApplicationQuit()
    {
        ShutdownNetwork();
    }

    /// <summary>
    /// Clean up when this object is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        ShutdownNetwork();
    }

    /// <summary>
    /// Clears callbacks and shuts the network down if needed.
    ///
    /// This method is guarded so it only runs once.
    /// </summary>
    private void ShutdownNetwork()
    {
        if (_hasShutdown)
        {
            return;
        }

        _hasShutdown = true;

        if (_networkManager == null)
        {
            return;
        }

        // Clear the approval callback for cleanliness.
        _networkManager.ConnectionApprovalCallback = null;

        // Only shut down if NGO is actually listening.
        if (_networkManager.IsListening)
        {
            _networkManager.Shutdown();
        }
    }
}