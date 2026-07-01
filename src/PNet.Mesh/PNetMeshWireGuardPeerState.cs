using System;
using System.Collections.Generic;
using System.Linq;

namespace PNet.Mesh
{
    public static class PNetMeshWireGuardLifecycle
    {
        public const ulong RekeyAfterMessages = 1UL << 60;
        public const ulong RejectAfterMessages = ulong.MaxValue - (1UL << 13);

        public static readonly TimeSpan RekeyAfterTime = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan RekeyAttemptTime = TimeSpan.FromSeconds(90);
        public static readonly TimeSpan RekeyTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan RejectAfterTime = TimeSpan.FromSeconds(180);
        public static readonly TimeSpan KeepaliveTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan CookieRefreshTime = TimeSpan.FromSeconds(120);
    }

    public enum PNetMeshWireGuardKeypairRole
    {
        Initiator = 0,
        Responder = 1
    }

    public enum PNetMeshWireGuardKeypairState
    {
        Next = 0,
        Current = 1,
        Previous = 2
    }

    public sealed class PNetMeshWireGuardKeypair : IDisposable
    {
        readonly PNetMeshPacketTracker _replayWindow = new PNetMeshPacketTracker();

        public PNetMeshWireGuardKeypair(
            uint localReceiverIndex,
            uint remoteReceiverIndex,
            PNetMeshWireGuardKeypairRole role,
            DateTimeOffset createdAt)
        {
            LocalReceiverIndex = localReceiverIndex;
            RemoteReceiverIndex = remoteReceiverIndex;
            Role = role;
            CreatedAt = createdAt;
            LastTransportActivity = createdAt;
        }

        public uint LocalReceiverIndex { get; }

        public uint RemoteReceiverIndex { get; }

        public PNetMeshWireGuardKeypairRole Role { get; }

        public PNetMeshWireGuardKeypairState State { get; internal set; } = PNetMeshWireGuardKeypairState.Next;

        public DateTimeOffset CreatedAt { get; }

        public DateTimeOffset LastTransportActivity { get; private set; }

        public ulong LastSentCounter { get; private set; }

        public ulong LastReceivedCounter { get; private set; }

        public PNetMeshWireGuardPeer Peer { get; internal set; }

        public void RecordTransportActivity(DateTimeOffset at)
        {
            if (at > LastTransportActivity)
                LastTransportActivity = at;
        }

        public void RecordSentCounter(ulong counter)
        {
            if (counter > LastSentCounter)
                LastSentCounter = counter;
        }

        public void RecordReceivedCounter(ulong counter)
        {
            if (counter > LastReceivedCounter)
                LastReceivedCounter = counter;
        }

        public bool TryAddReceivedCounter(ulong counter)
        {
            if (!_replayWindow.TryAdd(counter))
                return false;

            RecordReceivedCounter(counter);
            return true;
        }

        public bool ShouldRekey(DateTimeOffset now)
        {
            return now - CreatedAt >= PNetMeshWireGuardLifecycle.RekeyAfterTime
                   || LastSentCounter >= PNetMeshWireGuardLifecycle.RekeyAfterMessages;
        }

        public bool ShouldReject(DateTimeOffset now)
        {
            return now - CreatedAt >= PNetMeshWireGuardLifecycle.RejectAfterTime
                   || LastSentCounter >= PNetMeshWireGuardLifecycle.RejectAfterMessages
                   || LastReceivedCounter >= PNetMeshWireGuardLifecycle.RejectAfterMessages;
        }

        public bool ShouldSendKeepalive(DateTimeOffset now)
        {
            return now - LastTransportActivity >= PNetMeshWireGuardLifecycle.KeepaliveTimeout;
        }

        public void Dispose()
        {
            _replayWindow.Dispose();
        }
    }

    public sealed class PNetMeshWireGuardPeer
    {
        readonly byte[] _publicKey;

        internal PNetMeshWireGuardPeer(byte[] publicKey)
        {
            _publicKey = publicKey;
        }

        public byte[] PublicKey => (byte[])_publicKey.Clone();

        public PNetMeshWireGuardKeypair CurrentKeypair { get; internal set; }

        public PNetMeshWireGuardKeypair PreviousKeypair { get; internal set; }

        public PNetMeshWireGuardKeypair NextKeypair { get; internal set; }
    }

    public sealed class PNetMeshWireGuardPeerTable
    {
        readonly Dictionary<byte[], PNetMeshWireGuardPeer> _peers =
            new Dictionary<byte[], PNetMeshWireGuardPeer>(PNetMeshByteArrayComparer.Default);

        readonly Dictionary<uint, (PNetMeshWireGuardPeer Peer, PNetMeshWireGuardKeypair Keypair)> _receiverIndexes =
            new Dictionary<uint, (PNetMeshWireGuardPeer Peer, PNetMeshWireGuardKeypair Keypair)>();

        public PNetMeshWireGuardPeer GetOrAdd(ReadOnlySpan<byte> remotePublicKey)
        {
            if (remotePublicKey.Length != 32)
                throw new ArgumentOutOfRangeException(nameof(remotePublicKey));

            var key = remotePublicKey.ToArray();
            if (_peers.TryGetValue(key, out var peer))
                return peer;

            peer = new PNetMeshWireGuardPeer(key);
            _peers.Add(key, peer);
            return peer;
        }

        public PNetMeshWireGuardKeypair StageNextKeypair(
            ReadOnlySpan<byte> remotePublicKey,
            PNetMeshWireGuardKeypair keypair)
        {
            if (keypair == null) throw new ArgumentNullException(nameof(keypair));

            var peer = GetOrAdd(remotePublicKey);
            var oldNext = peer.NextKeypair;
            Unindex(oldNext);
            if (oldNext != null && !ReferenceEquals(oldNext, keypair))
                oldNext.Dispose();

            keypair.State = PNetMeshWireGuardKeypairState.Next;
            peer.NextKeypair = keypair;
            Index(peer, keypair);
            return keypair;
        }

        public bool PromoteNextKeypair(ReadOnlySpan<byte> remotePublicKey)
        {
            var peer = GetOrAdd(remotePublicKey);
            if (peer.NextKeypair == null)
                return false;

            var next = peer.NextKeypair;
            peer.NextKeypair = null;
            SetCurrent(peer, next);
            return true;
        }

        public PNetMeshWireGuardKeypair SetCurrentKeypair(
            ReadOnlySpan<byte> remotePublicKey,
            PNetMeshWireGuardKeypair keypair)
        {
            if (keypair == null) throw new ArgumentNullException(nameof(keypair));

            return SetCurrent(GetOrAdd(remotePublicKey), keypair);
        }

        public bool TryGetByReceiverIndex(
            uint receiverIndex,
            out PNetMeshWireGuardPeer peer,
            out PNetMeshWireGuardKeypair keypair)
        {
            if (_receiverIndexes.TryGetValue(receiverIndex, out var entry))
            {
                peer = entry.Peer;
                keypair = entry.Keypair;
                return true;
            }

            peer = null;
            keypair = null;
            return false;
        }

        public void RemoveExpiredKeypairs(DateTimeOffset now)
        {
            foreach (var peer in _peers.Values.ToArray())
            {
                if (peer.PreviousKeypair?.ShouldReject(now) == true)
                {
                    Unindex(peer.PreviousKeypair);
                    peer.PreviousKeypair.Dispose();
                    peer.PreviousKeypair = null;
                }

                if (peer.NextKeypair?.ShouldReject(now) == true)
                {
                    Unindex(peer.NextKeypair);
                    peer.NextKeypair.Dispose();
                    peer.NextKeypair = null;
                }
            }
        }

        PNetMeshWireGuardKeypair SetCurrent(PNetMeshWireGuardPeer peer, PNetMeshWireGuardKeypair keypair)
        {
            var oldPrevious = peer.PreviousKeypair;
            if (oldPrevious != null && !ReferenceEquals(oldPrevious, peer.CurrentKeypair))
            {
                Unindex(oldPrevious);
                oldPrevious.Dispose();
                peer.PreviousKeypair = null;
            }

            Unindex(peer.CurrentKeypair);
            Unindex(peer.NextKeypair);

            if (peer.CurrentKeypair != null)
            {
                peer.CurrentKeypair.State = PNetMeshWireGuardKeypairState.Previous;
                peer.PreviousKeypair = peer.CurrentKeypair;
                Index(peer, peer.PreviousKeypair);
            }

            if (!ReferenceEquals(peer.NextKeypair, keypair))
                peer.NextKeypair = null;

            keypair.State = PNetMeshWireGuardKeypairState.Current;
            peer.CurrentKeypair = keypair;
            Index(peer, keypair);
            return keypair;
        }

        void Index(PNetMeshWireGuardPeer peer, PNetMeshWireGuardKeypair keypair)
        {
            if (keypair != null)
            {
                keypair.Peer = peer;
                _receiverIndexes[keypair.LocalReceiverIndex] = (peer, keypair);
            }
        }

        void Unindex(PNetMeshWireGuardKeypair keypair)
        {
            if (keypair != null
                && _receiverIndexes.TryGetValue(keypair.LocalReceiverIndex, out var entry)
                && ReferenceEquals(entry.Keypair, keypair))
                _receiverIndexes.Remove(keypair.LocalReceiverIndex);
        }
    }
}
