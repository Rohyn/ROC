using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// Controls the early client-side application flow:
/// Bootstrap -> Branding Splash -> Main Menu -> Connect on Play
///
/// This script is intended to live on the persistent AppRoot object in the Bootstrap scene.
/// AppRoot should also contain:
/// - NetworkManager
/// - UnityTransport
/// - BootstrapNetwork
///
/// Design intent:
/// - Keep process-lifetime systems alive across scene changes
/// - Keep branding/menu flow completely separate from networked gameplay flow
/// - Do NOT connect to the server until the player explicitly presses Play
/// </summary>
public class AppFlowController : MonoBehaviour
{
    /// <summary>
    /// Simple global access point so menu scripts in later scenes can find us.
    /// This is useful because AppRoot lives in the DontDestroyOnLoad scene,
    /// not inside the menu scene itself.
    /// </summary>
    public static AppFlowController Instance { get; private set; }

    /// <summary>
    /// UI-facing event that tells menu views when connection state/status has changed.
    ///
    /// Parameters:
    /// - bool isConnecting
    /// - string statusMessage
    ///
    /// Example:
    /// - (false, "Ready")
    /// - (true,  "Connecting...")
    /// - (false, "Connection failed: Server is full.")
    /// </summary>
    public event Action<bool, string> MenuConnectionStateChanged;

    [Header("Scene Names")]
    [Tooltip("The first non-bootstrap scene shown to client builds.")]
    [SerializeField] private string brandSplashSceneName = "BrandSplash";

    [Tooltip("The main menu scene shown after the splash screen.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Splash Timing")]
    [Tooltip("How long the branding screen should remain visible before moving to the main menu.")]
    [SerializeField] private float splashDurationSeconds = 2.5f;

    [Header("Dependencies")]
    [Tooltip("Reference to the BootstrapNetwork component on AppRoot.")]
    [SerializeField] private BootstrapNetwork bootstrapNetwork;

    /// <summary>
    /// Tracks whether we are currently trying to connect.
    /// Prevents duplicate Play presses from starting multiple connection attempts.
    /// </summary>
    private bool _isConnecting;

    /// <summary>
    /// Holds the latest user-facing status line for the main menu.
    /// MainMenuView can read this when it first appears.
    /// </summary>
    private string _currentStatusMessage = "Ready";

    /// <summary>
    /// Public read-only access so UI can query the current state immediately
    /// when it first loads or subscribes.
    /// </summary>
    public bool IsConnecting => _isConnecting;

    /// <summary>
    /// Public read-only access to the latest status message.
    /// </summary>
    public string CurrentStatusMessage => _currentStatusMessage;

    /// <summary>
    /// Prevents accidental duplicate AppRoot instances if the Bootstrap scene
    /// is ever loaded again by mistake.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[AppFlowController] Duplicate AppFlowController found. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Keep AppRoot alive when moving from Bootstrap -> Splash -> Main Menu.
        DontDestroyOnLoad(gameObject);

        // If not assigned in the Inspector, try to find it on the same GameObject.
        if (bootstrapNetwork == null)
        {
            bootstrapNetwork = GetComponent<BootstrapNetwork>();
        }
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleClientSceneEventDebug;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleClientSceneEventDebug;
        }
    }

    private void HandleClientSceneEventDebug(SceneEvent sceneEvent)
    {
        Debug.Log($"[AppFlowController] Client Scene Event: type={sceneEvent.SceneEventType}, scene={sceneEvent.SceneName}, clientId={sceneEvent.ClientId}");
    }

    private void Start()
    {
#if UNITY_SERVER
        // Dedicated server builds do not need splash or menu flow.
        // BootstrapNetwork will handle server startup.
        return;
#else
        // Client builds move through splash and menu before any network connection attempt.
        StartCoroutine(RunInitialClientFlow());
#endif
    }

    /// <summary>
    /// Drives the initial client-only flow.
    ///
    /// Sequence:
    /// 1. Load branding scene
    /// 2. Wait a short amount of real time
    /// 3. Load main menu scene
    /// </summary>
    private IEnumerator RunInitialClientFlow()
    {
        // Load the branding scene first.
        yield return LoadSceneSingleAsync(brandSplashSceneName);

        // Wait in real time so splash timing is not affected by game timescale.
        yield return new WaitForSecondsRealtime(splashDurationSeconds);

        // Move to the main menu scene.
        yield return LoadSceneSingleAsync(mainMenuSceneName);

        // Once the main menu is visible, push the current state to any listening UI.
        NotifyMenuConnectionStateChanged();
    }

    /// <summary>
    /// Public method for the Play button.
    ///
    /// This should:
    /// - prevent double-click re-entry
    /// - subscribe to connection callbacks
    /// - ask BootstrapNetwork to start the client
    ///
    /// IMPORTANT:
    /// We do NOT manually load a gameplay scene here.
    /// Later, once connected, gameplay scene transitions should be controlled
    /// by the networked/server flow.
    /// </summary>
    public void OnPlayPressed()
    {
        if (_isConnecting)
        {
            Debug.Log("[AppFlowController] Ignoring Play press because a connection attempt is already in progress.");
            return;
        }

        if (bootstrapNetwork == null)
        {
            Debug.LogError("[AppFlowController] Cannot start client because BootstrapNetwork is missing.");
            SetConnectionState(false, "Connection failed: missing network bootstrap.");
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("[AppFlowController] Cannot start client because NetworkManager.Singleton is null.");
            SetConnectionState(false, "Connection failed: NetworkManager not found.");
            return;
        }

        // Immediately update menu UI so the player sees feedback and buttons disable.
        SetConnectionState(true, "Connecting...");

        // Subscribe before starting the client so we catch the result of this attempt.
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        bool started = bootstrapNetwork.StartClient();

        if (!started)
        {
            Debug.LogWarning("[AppFlowController] Client failed to begin connecting.");
            CleanupConnectionCallbacks();
            SetConnectionState(false, "Connection failed: client could not start.");
            return;
        }

        Debug.Log("[AppFlowController] Client connection attempt started.");
    }

    /// <summary>
    /// Public method for the Quit button.
    ///
    /// In the editor, Application.Quit() does nothing, which is expected.
    /// In a built player, it closes the application.
    /// </summary>
    public void OnQuitPressed()
    {
        Debug.Log("[AppFlowController] Quit requested.");
        Application.Quit();
    }

    /// <summary>
    /// Called by NGO when the local client successfully connects.
    ///
    /// This callback fires on:
    /// - the server when a client connects
    /// - the local client that connected
    ///
    /// We only care about the local client's successful connection here.
    /// </summary>
    private void HandleClientConnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        Debug.Log($"[AppFlowController] OnClientConnectedCallback fired. callbackClientId={clientId}, localClientId={networkManager.LocalClientId}");

        CleanupConnectionCallbacks();

        SetConnectionState(false, "Connected. Waiting for server...");
    }

    /// <summary>
    /// Called by NGO when the local client disconnects.
    ///
    /// This can happen if:
    /// - the server is unavailable
    /// - approval failed
    /// - the connection was interrupted
    /// - the server shut down
    /// </summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            CleanupConnectionCallbacks();
            SetConnectionState(false, "Connection failed.");
            return;
        }

        Debug.Log($"[AppFlowController] OnClientDisconnectCallback fired. callbackClientId={clientId}, localClientId={networkManager.LocalClientId}");

        string disconnectReason = networkManager.DisconnectReason;

        if (string.IsNullOrWhiteSpace(disconnectReason))
        {
            Debug.LogWarning("[AppFlowController] Local client disconnected.");
            SetConnectionState(false, "Connection failed.");
        }
        else
        {
            Debug.LogWarning($"[AppFlowController] Local client disconnected. Reason: {disconnectReason}");
            SetConnectionState(false, $"Connection failed: {disconnectReason}");
        }

        CleanupConnectionCallbacks();
    }

    /// <summary>
    /// Central helper for changing connection state and notifying UI.
    ///
    /// Keeping this in one method ensures:
    /// - the boolean and status string stay in sync
    /// - UI notifications are always sent consistently
    /// </summary>
    private void SetConnectionState(bool isConnecting, string statusMessage)
    {
        _isConnecting = isConnecting;
        _currentStatusMessage = statusMessage;
        NotifyMenuConnectionStateChanged();
    }

    /// <summary>
    /// Invokes the UI event with the current stored values.
    /// Any menu currently listening can update itself immediately.
    /// </summary>
    private void NotifyMenuConnectionStateChanged()
    {
        MenuConnectionStateChanged?.Invoke(_isConnecting, _currentStatusMessage);
    }

    /// <summary>
    /// Helper that loads a scene in Single mode and waits until it is finished.
    ///
    /// Single mode is appropriate here because:
    /// - we want one visible presentation scene at a time
    /// - AppRoot survives separately via DontDestroyOnLoad
    /// </summary>
    private IEnumerator LoadSceneSingleAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[AppFlowController] Tried to load a scene with an empty name.");
            yield break;
        }

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

        if (operation == null)
        {
            Debug.LogError($"[AppFlowController] Failed to start loading scene '{sceneName}'.");
            yield break;
        }

        while (!operation.isDone)
        {
            yield return null;
        }

        Debug.Log($"[AppFlowController] Loaded scene '{sceneName}'.");
    }

    /// <summary>
    /// Unsubscribes from NGO callbacks.
    ///
    /// We keep this in one place so it is easy to stay consistent and avoid
    /// duplicate subscriptions across multiple connection attempts.
    /// </summary>
    private void CleanupConnectionCallbacks()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        CleanupConnectionCallbacks();
    }
}