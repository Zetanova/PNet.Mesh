using System;
using System.Collections.Immutable;

namespace PNet.Mesh
{
    public sealed class PNetMeshRelayPacket
    {
        /// <summary>
        /// destination address (hashed)
        /// not-set indicate a broadcast
        /// </summary>
        public byte[]? Address { get; init; }

        /// <summary>
        /// senders relay sequence number
        /// duplication detection over sender + seq_number
        /// </summary>
        public ulong SeqNumber { get; init; }

        /// <summary>
        /// decrementing hop count
        /// at zero the receiving node should only relay to a known peer
        /// local to remote to known-remote: 0
        /// local to remote to remote to known-remote: 1
        /// local to cluster: 0
        /// local to cluster to known-remote: 1
        /// local to cluster1 to cluster2 to known-remote: 2
        /// broadcast to everyone: 3-4
        /// </summary>
        public ushort HopCount { get; init; }

        /// <summary>
        /// route of relay starting with sender
        /// </summary>
        public ImmutableArray<byte[]> Route { get; init; }

        public ReadOnlyMemory<byte> Payload { get; init; }


        public PNetMeshCandidateExchange? CandidateExchange { get; init; }
    }
}
