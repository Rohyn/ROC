using System.Collections;
using System.Collections.Generic;
using ROC.Inventory;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ROC.Persistence
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerAccountState))]
    public class PlayerPersistenceRoot : NetworkBehaviour
    {
        [Header("Dev Identity")]
        [SerializeField] private bool useOwnerClientIdInDevIds = true;
        [SerializeField] private string devAccountId = "local_dev_account";
        [SerializeField] private string devCharacterId = "local_dev_character";
        [SerializeField] private string defaultCharacterName = "Dev Character";

        [Header("Starting Account Values")]
        [SerializeField] private int startingPotential = 3;
        [SerializeField] private int startingInspiration = 0;

        [Header("Save Timing")]
        [SerializeField] private float saveDebounceSeconds = 1f;
        [SerializeField] private bool saveOnSpawnAfterLoad = false;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private JsonFileSaveStore _saveStore;

        private PlayerAccountState _accountState;
        private PlayerInventory _inventory;
        private global::PlayerProgressState _progressState;
        private global::PlayerAreaStreamingController _areaStreamingController;

        private string _activeAccountId;
        private string _activeCharacterId;

        private string _explicitAccountId;
        private string _explicitCharacterId;
        private bool _hasExplicitIdentity;

        private string _loadedCharacterSceneId;
        private bool _loadedExistingCharacter;
        private bool _loaded;
        private bool _saveQueued;
        private bool _appliedLoadedAreaToStreamingController;
        private float _nextSaveTime;

        public bool IsLoaded => _loaded;
        public bool LoadedExistingCharacter => _loadedExistingCharacter;
        public string LoadedCharacterSceneId => _loadedCharacterSceneId;

        public string GetAccountIdForClient(ulong ownerClientId)
        {
            return useOwnerClientIdInDevIds
                ? $"{devAccountId}_{ownerClientId}"
                : devAccountId;
        }

        public string GetCharacterIdForClient(ulong ownerClientId)
        {
            return useOwnerClientIdInDevIds
                ? $"{devCharacterId}_{ownerClientId}"
                : devCharacterId;
        }

        public void InitializeIdentityServer(string accountId, string characterId)
        {
            if (IsSpawned)
            {
                Debug.LogWarning("[PlayerPersistenceRoot] InitializeIdentityServer should be called before spawn.", this);
            }

            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(characterId))
            {
                Debug.LogWarning("[PlayerPersistenceRoot] Refused invalid explicit identity.", this);
                return;
            }

            _explicitAccountId = accountId;
            _explicitCharacterId = characterId;
            _hasExplicitIdentity = true;
        }

        private void Awake()
        {
            _accountState = GetComponent<PlayerAccountState>();
            _inventory = GetComponent<PlayerInventory>();
            _progressState = GetComponent<global::PlayerProgressState>();
            _areaStreamingController = GetComponent<global::PlayerAreaStreamingController>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            _saveStore = new JsonFileSaveStore();

            ResolveIdentity();
            LoadOrCreateState();
            SubscribeToStateEvents();

            StartCoroutine(ApplyLoadedAreaWhenReady());

            if (saveOnSpawnAfterLoad)
            {
                QueueSave();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
            {
                return;
            }

            UnsubscribeFromStateEvents();
            SaveNow();
        }

        private void Update()
        {
            if (!IsServer || !_loaded || !_saveQueued)
            {
                return;
            }

            if (Time.unscaledTime < _nextSaveTime)
            {
                return;
            }

            SaveNow();
        }

        public void QueueSave()
        {
            if (!IsServer || !_loaded)
            {
                return;
            }

            _saveQueued = true;
            _nextSaveTime = Time.unscaledTime + Mathf.Max(0.05f, saveDebounceSeconds);
        }

        public void SaveNow()
        {
            if (!IsServer || !_loaded || _saveStore == null)
            {
                return;
            }

            AccountSaveData account = BuildAccountSaveData();
            CharacterSaveData character = BuildCharacterSaveData();

            _saveStore.SaveAccount(account);
            _saveStore.SaveCharacter(character);

            _saveQueued = false;

            if (verboseLogging)
            {
                Debug.Log(
                    $"[PlayerPersistenceRoot] Saved account '{_activeAccountId}' and character '{_activeCharacterId}'.",
                    this);
            }
        }

        private void ResolveIdentity()
        {
            if (_hasExplicitIdentity)
            {
                _activeAccountId = _explicitAccountId;
                _activeCharacterId = _explicitCharacterId;
                return;
            }

            _activeAccountId = GetAccountIdForClient(OwnerClientId);
            _activeCharacterId = GetCharacterIdForClient(OwnerClientId);
        }

        private void LoadOrCreateState()
        {
            AccountSaveData account = LoadOrCreateAccount();
            CharacterSaveData character = LoadOrCreateCharacter(account);

            if (_accountState != null)
            {
                _accountState.ApplyLoadedDataServer(account);
            }

            ApplyCharacterData(character);

            _loaded = true;

            if (verboseLogging)
            {
                string characterKind = _loadedExistingCharacter ? "existing" : "new";

                Debug.Log(
                    $"[PlayerPersistenceRoot] Loaded {characterKind} account '{_activeAccountId}' and character '{_activeCharacterId}'. Scene='{_loadedCharacterSceneId}'.",
                    this);
            }
        }

        private AccountSaveData LoadOrCreateAccount()
        {
            if (_saveStore.TryLoadAccount(_activeAccountId, out AccountSaveData account) && account != null)
            {
                EnsureAccountHasCharacter(account, _activeCharacterId);
                return account;
            }

            account = new AccountSaveData
            {
                AccountId = _activeAccountId,
                Potential = startingPotential,
                Inspiration = startingInspiration,
                CharacterIds = new List<string> { _activeCharacterId }
            };

            return account;
        }

        private CharacterSaveData LoadOrCreateCharacter(AccountSaveData account)
        {
            if (_saveStore.TryLoadCharacter(_activeCharacterId, out CharacterSaveData character) && character != null)
            {
                _loadedExistingCharacter = true;
                _loadedCharacterSceneId = character.SceneId;
                return character;
            }

            _loadedExistingCharacter = false;

            character = new CharacterSaveData
            {
                CharacterId = _activeCharacterId,
                AccountId = account.AccountId,
                CharacterName = defaultCharacterName,
                SceneId = GetCurrentSceneIdForSave(),
                Position = new SerializableVector3(transform.position),
                Yaw = transform.eulerAngles.y
            };

            _loadedCharacterSceneId = character.SceneId;
            return character;
        }

        private void ApplyCharacterData(CharacterSaveData character)
        {
            if (character == null)
            {
                return;
            }

            _loadedCharacterSceneId = character.SceneId;

            ApplyTransform(character);

            if (_inventory != null)
            {
                _inventory.ApplySaveDataServer(character.BagItems, character.EquippedItems);
            }

            if (_progressState != null)
            {
                _progressState.ReplaceFlagsFromSaveServer(character.ProgressFlags);
            }
        }

        private void ApplyTransform(CharacterSaveData character)
        {
            if (character == null)
            {
                return;
            }

            Vector3 position = character.Position.ToVector3();
            Quaternion rotation = Quaternion.Euler(0f, character.Yaw, 0f);

            CharacterController controller = GetComponent<CharacterController>();

            if (controller != null)
            {
                controller.enabled = false;
                transform.SetPositionAndRotation(position, rotation);
                controller.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"[PlayerPersistenceRoot] Applied saved transform. Position={transform.position}, Yaw={character.Yaw:F1}.",
                    this);
            }
        }

        private IEnumerator ApplyLoadedAreaWhenReady()
        {
            if (!IsServer)
            {
                yield break;
            }

            const int maxFramesToWait = 10;

            for (int i = 0; i < maxFramesToWait; i++)
            {
                if (TryApplyLoadedAreaToStreamingController())
                {
                    yield break;
                }

                yield return null;
            }

            if (verboseLogging && _loadedExistingCharacter && !_appliedLoadedAreaToStreamingController)
            {
                Debug.LogWarning(
                    $"[PlayerPersistenceRoot] Could not apply loaded area '{_loadedCharacterSceneId}' to PlayerAreaStreamingController after waiting.",
                    this);
            }
        }

        private bool TryApplyLoadedAreaToStreamingController()
        {
            if (!IsServer || !_loaded || _appliedLoadedAreaToStreamingController)
            {
                return false;
            }

            if (!_loadedExistingCharacter)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_loadedCharacterSceneId))
            {
                return false;
            }

            if (_areaStreamingController == null)
            {
                _areaStreamingController = GetComponent<global::PlayerAreaStreamingController>();
            }

            if (_areaStreamingController == null)
            {
                return false;
            }

            if (!_areaStreamingController.HasInitializedAreaState)
            {
                return false;
            }

            _areaStreamingController.SetCurrentAreaFromPersistenceServer(_loadedCharacterSceneId, true);
            _appliedLoadedAreaToStreamingController = true;

            return true;
        }

        private AccountSaveData BuildAccountSaveData()
        {
            AccountSaveData account = _accountState != null
                ? _accountState.CreateSaveData()
                : new AccountSaveData();

            account.AccountId = _activeAccountId;
            EnsureAccountHasCharacter(account, _activeCharacterId);

            return account;
        }

        private CharacterSaveData BuildCharacterSaveData()
        {
            CharacterSaveData character = new CharacterSaveData
            {
                CharacterId = _activeCharacterId,
                AccountId = _activeAccountId,
                CharacterName = defaultCharacterName,
                SceneId = GetCurrentSceneIdForSave(),
                Position = new SerializableVector3(transform.position),
                Yaw = transform.eulerAngles.y
            };

            if (_inventory != null)
            {
                character.BagItems = _inventory.CreateInventorySaveData(PlayerInventory.InventoryCollection.Bag);
                character.EquippedItems = _inventory.CreateInventorySaveData(PlayerInventory.InventoryCollection.Equipped);
            }

            if (_progressState != null)
            {
                character.ProgressFlags = _progressState.CreateFlagSaveData();
            }

            return character;
        }

        private string GetCurrentSceneIdForSave()
        {
            if (_areaStreamingController == null)
            {
                _areaStreamingController = GetComponent<global::PlayerAreaStreamingController>();
            }

            if (_areaStreamingController != null &&
                !string.IsNullOrWhiteSpace(_areaStreamingController.CurrentAreaSceneName))
            {
                return _areaStreamingController.CurrentAreaSceneName;
            }

            if (gameObject.scene.IsValid() && !string.IsNullOrWhiteSpace(gameObject.scene.name))
            {
                return gameObject.scene.name;
            }

            return SceneManager.GetActiveScene().name;
        }

        private void SubscribeToStateEvents()
        {
            if (_accountState != null)
            {
                _accountState.AccountChanged += QueueSave;
            }

            if (_inventory != null)
            {
                _inventory.InventoryChanged += QueueSave;
            }

            if (_progressState != null)
            {
                _progressState.FlagsChanged += QueueSave;
            }
        }

        private void UnsubscribeFromStateEvents()
        {
            if (_accountState != null)
            {
                _accountState.AccountChanged -= QueueSave;
            }

            if (_inventory != null)
            {
                _inventory.InventoryChanged -= QueueSave;
            }

            if (_progressState != null)
            {
                _progressState.FlagsChanged -= QueueSave;
            }
        }

        private static void EnsureAccountHasCharacter(AccountSaveData account, string characterId)
        {
            if (account == null)
            {
                return;
            }

            if (account.CharacterIds == null)
            {
                account.CharacterIds = new List<string>();
            }

            if (!account.CharacterIds.Contains(characterId))
            {
                account.CharacterIds.Add(characterId);
            }
        }
    }
}