using System;
using Unity.Collections;
using Unity.Netcode;

namespace ROC.Inventory
{
    /// <summary>
    /// Lightweight replicated inventory stack.
    /// </summary>
    [Serializable]
    public struct ReplicatedInventoryEntry : INetworkSerializable, IEquatable<ReplicatedInventoryEntry>
    {
        public FixedString64Bytes ItemId;
        public int Quantity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Quantity);
        }

        public bool Equals(ReplicatedInventoryEntry other)
        {
            return ItemId.Equals(other.ItemId) && Quantity == other.Quantity;
        }

        public override bool Equals(object obj)
        {
            return obj is ReplicatedInventoryEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ItemId, Quantity);
        }
    }
}