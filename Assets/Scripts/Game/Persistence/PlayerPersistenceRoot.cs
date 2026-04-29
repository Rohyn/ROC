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

        private string _activeAccountId;
        private string _activeCharacterId;

        private bool _loaded;
        private bool _saveQueued;
        private float _nextSaveTime;

        private void Awake()
        {
            _accountState = GetComponent<PlayerAccountState>();
            _inventory = GetComponent<PlayerInventory>();
            _progressState = GetComponent<global::PlayerProgressState>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            _saveStore = new JsonFileSaveStore();

            ResolveDevIdentity();
            LoadOrCreateState();

            SubscribeToStateEvents();

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
                Debug.Log($"[PlayerPersistenceRoot] Saved account '{_activeAccountId}' and character '{_activeCharacterId}'.", this);
            }
        }

        private void ResolveDevIdentity()
        {
            if (useOwnerClientIdInDevIds)
            {
                _activeAccountId = $"{devAccountId}_{OwnerClientId}";
                _activeCharacterId = $"{devCharacterId}_{OwnerClientId}";
            }
            else
            {
                _activeAccountId = devAccountId;
                _activeCharacterId = devCharacterId;
            }
        }

        private void LoadOrCreateState()
        {
            AccountSaveData account = LoadOrCreateAccount();
            CharacterSaveData character = LoadOrCreateCharacter(account);

            _accountState.ApplyLoadedDataServer(account);
            ApplyCharacterData(character);

            _loaded = true;

            if (verboseLogging)
            {
                Debug.Log($"[PlayerPersistenceRoot] Loaded account '{_activeAccountId}' and character '{_activeCharacterId}'.", this);
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
                return character;
            }

            return new CharacterSaveData
            {
                CharacterId = _activeCharacterId,
                AccountId = account.AccountId,
                CharacterName = defaultCharacterName,
                SceneId = SceneManager.GetActiveScene().name,
                Position = new SerializableVector3(transform.position),
                Yaw = transform.eulerAngles.y
            };
        }

        private void ApplyCharacterData(CharacterSaveData character)
        {
            if (character == null)
            {
                return;
            }

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
        }

        private AccountSaveData BuildAccountSaveData()
        {
            AccountSaveData account = _accountState.CreateSaveData();
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
                SceneId = SceneManager.GetActiveScene().name,
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