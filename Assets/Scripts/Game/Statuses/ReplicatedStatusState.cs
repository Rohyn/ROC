using System;
using Unity.Collections;
using Unity.Netcode;

namespace ROC.Statuses
{
    /// <summary>
    /// Lightweight replicated status snapshot.
    ///
    /// This is what gets synchronized to clients.
    ///
    /// IMPORTANT:
    /// - We replicate StatusId, stack count, and an absolute expiry time in ServerTime.
    /// - We do NOT replicate ScriptableObject references.
    /// - We do NOT replicate every frame of remaining duration.
    ///
    /// If ExpireAtServerTimeSeconds <= 0, the status is treated as non-timed.
    /// </summary>
    [Serializable]
    public struct ReplicatedStatusState : INetworkSerializable, IEquatable<ReplicatedStatusState>
    {
        public FixedString64Bytes StatusId;
        public int Stacks;
        public double ExpireAtServerTimeSeconds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref StatusId);
            serializer.SerializeValue(ref Stacks);
            serializer.SerializeValue(ref ExpireAtServerTimeSeconds);
        }

        public bool Equals(ReplicatedStatusState other)
        {
            return StatusId.Equals(other.StatusId) &&
                   Stacks == other.Stacks &&
                   ExpireAtServerTimeSeconds.Equals(other.ExpireAtServerTimeSeconds);
        }

        public override bool Equals(object obj)
        {
            return obj is ReplicatedStatusState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StatusId, Stacks, ExpireAtServerTimeSeconds);
        }
    }
}