using System;
using Unity.Netcode;
using UnityEngine;

namespace ROC.Persistence
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerAccountState : NetworkBehaviour
    {
        private readonly NetworkVariable<int> _potential = new(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _inspiration = new(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        public event Action AccountChanged;

        public string AccountId { get; private set; }

        public int Potential => _potential.Value;
        public int Inspiration => _inspiration.Value;

        public override void OnNetworkSpawn()
        {
            _potential.OnValueChanged += HandleAccountValueChanged;
            _inspiration.OnValueChanged += HandleAccountValueChanged;
        }

        public override void OnNetworkDespawn()
        {
            _potential.OnValueChanged -= HandleAccountValueChanged;
            _inspiration.OnValueChanged -= HandleAccountValueChanged;
        }

        public void ApplyLoadedDataServer(AccountSaveData account)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[PlayerAccountState] ApplyLoadedDataServer called on non-server instance.", this);
                return;
            }

            if (account == null)
            {
                Debug.LogWarning("[PlayerAccountState] Cannot apply null account data.", this);
                return;
            }

            AccountId = account.AccountId;
            _potential.Value = Mathf.Max(0, account.Potential);
            _inspiration.Value = Mathf.Max(0, account.Inspiration);

            AccountChanged?.Invoke();
        }

        public AccountSaveData CreateSaveData()
        {
            return new AccountSaveData
            {
                AccountId = AccountId,
                Potential = _potential.Value,
                Inspiration = _inspiration.Value
            };
        }

        public bool TrySpendPotential(int amount)
        {
            if (!IsServer)
            {
                return false;
            }

            if (amount <= 0)
            {
                return false;
            }

            if (_potential.Value < amount)
            {
                return false;
            }

            _potential.Value -= amount;
            AccountChanged?.Invoke();
            return true;
        }

        public void AddPotential(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }

            _potential.Value += amount;
            AccountChanged?.Invoke();
        }

        public bool TrySpendInspiration(int amount)
        {
            if (!IsServer)
            {
                return false;
            }

            if (amount <= 0)
            {
                return false;
            }

            if (_inspiration.Value < amount)
            {
                return false;
            }

            _inspiration.Value -= amount;
            AccountChanged?.Invoke();
            return true;
        }

        public void AddInspiration(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }

            _inspiration.Value += amount;
            AccountChanged?.Invoke();
        }

        private void HandleAccountValueChanged(int previousValue, int newValue)
        {
            AccountChanged?.Invoke();
        }
    }
}