using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ROC.Session
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerSession : NetworkBehaviour
    {
        [Header("Character Select")]
        [SerializeField] private string characterSelectSceneName = "CharacterSelect";
        [SerializeField] private bool loadCharacterSelectOnOwnerSpawn = true;
        [SerializeField] private bool unloadCharacterSelectWhenGameplayStarts = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private bool _selectionLockedServer;

        public static PlayerSession Local { get; private set; }

        public bool IsSelectionLockedServer => _selectionLockedServer;
        public string CharacterSelectSceneName => characterSelectSceneName;

        public override void OnNetworkSpawn()
        {
            DontDestroyOnLoad(gameObject);

            NetworkObject.ActiveSceneSynchronization = false;
            NetworkObject.SceneMigrationSynchronization = false;

            if (IsOwner)
            {
                Local = this;

                if (loadCharacterSelectOnOwnerSpawn)
                {
                    StartCoroutine(LoadCharacterSelectSceneRoutine());
                }
            }

            if (verboseLogging)
            {
                Debug.Log($"[PlayerSession] Spawned. OwnerClientId={OwnerClientId}, IsOwner={IsOwner}", this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this)
            {
                Local = null;
            }
        }

        public void RequestContinueWithDefaultCharacter()
        {
            if (!IsOwner)
            {
                Debug.LogWarning("[PlayerSession] Non-owner attempted to request character continue.", this);
                return;
            }

            ContinueWithDefaultCharacterRpc();
        }

        [Rpc(SendTo.Server)]
        private void ContinueWithDefaultCharacterRpc()
        {
            if (_selectionLockedServer)
            {
                return;
            }

            ServerCharacterSessionManager manager = FindFirstObjectByType<ServerCharacterSessionManager>();

            if (manager == null)
            {
                Debug.LogError("[PlayerSession] No ServerCharacterSessionManager found.");
                NotifyCharacterSelectionFailedOwnerRpc("Server session manager not found.");
                return;
            }

            manager.HandleContinueRequested(this);
        }

        public void MarkSelectionLockedServer()
        {
            if (!IsServer)
            {
                return;
            }

            _selectionLockedServer = true;
        }

        public void UnlockSelectionServer()
        {
            if (!IsServer)
            {
                return;
            }

            _selectionLockedServer = false;
        }

        [Rpc(SendTo.Owner)]
        public void NotifyCharacterSelectionAcceptedOwnerRpc()
        {
            if (verboseLogging)
            {
                Debug.Log("[PlayerSession] Character selection accepted by server.", this);
            }

            if (unloadCharacterSelectWhenGameplayStarts)
            {
                StartCoroutine(UnloadCharacterSelectSceneRoutine());
            }
        }

        [Rpc(SendTo.Owner)]
        public void NotifyCharacterSelectionFailedOwnerRpc(string reason)
        {
            if (verboseLogging)
            {
                Debug.LogWarning($"[PlayerSession] Character selection failed: {reason}", this);
            }
        }

        private IEnumerator LoadCharacterSelectSceneRoutine()
        {
            if (string.IsNullOrWhiteSpace(characterSelectSceneName))
            {
                yield break;
            }

            Scene existingScene = SceneManager.GetSceneByName(characterSelectSceneName);

            if (existingScene.IsValid() && existingScene.isLoaded)
            {
                yield break;
            }

            AsyncOperation loadOperation =
                SceneManager.LoadSceneAsync(characterSelectSceneName, LoadSceneMode.Additive);

            if (loadOperation == null)
            {
                Debug.LogError($"[PlayerSession] Failed to load character select scene '{characterSelectSceneName}'.", this);
                yield break;
            }

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            Scene loadedScene = SceneManager.GetSceneByName(characterSelectSceneName);

            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                SceneManager.SetActiveScene(loadedScene);
            }

            if (verboseLogging)
            {
                Debug.Log($"[PlayerSession] Loaded character select scene '{characterSelectSceneName}'.", this);
            }
        }

        private IEnumerator UnloadCharacterSelectSceneRoutine()
        {
            if (string.IsNullOrWhiteSpace(characterSelectSceneName))
            {
                yield break;
            }

            Scene scene = SceneManager.GetSceneByName(characterSelectSceneName);

            if (!scene.IsValid() || !scene.isLoaded)
            {
                yield break;
            }

            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(characterSelectSceneName);

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
                Debug.Log($"[PlayerSession] Unloaded character select scene '{characterSelectSceneName}'.", this);
            }
        }
    }
}