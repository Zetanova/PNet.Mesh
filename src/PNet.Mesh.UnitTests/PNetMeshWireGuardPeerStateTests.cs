using KeyPair = PNet.Mesh.PNetMeshKeyPair;
using PNet.Mesh;
using System;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshWireGuardPeerStateTests
    {
        [Fact]
        public void peer_table_tracks_receiver_indexes_and_keypair_rotation()
        {
            var table = new PNetMeshWireGuardPeerTable();
            var remotePublicKey = Enumerable.Range(0, 32).Select(i => (byte)(0x40 + i)).ToArray();
            var createdAt = DateTimeOffset.UnixEpoch;
            var first = new PNetMeshWireGuardKeypair(10, 20, PNetMeshWireGuardKeypairRole.Initiator, createdAt);
            var second = new PNetMeshWireGuardKeypair(11, 21, PNetMeshWireGuardKeypairRole.Initiator, createdAt.AddSeconds(1));

            var peer = table.GetOrAdd(remotePublicKey);
            table.StageNextKeypair(remotePublicKey, first);

            Assert.Same(first, peer.NextKeypair);
            Assert.True(table.TryGetByReceiverIndex(10, out var foundPeer, out var foundKeypair));
            Assert.Same(peer, foundPeer);
            Assert.Same(first, foundKeypair);
            Assert.Equal(PNetMeshWireGuardKeypairState.Next, foundKeypair.State);

            table.PromoteNextKeypair(remotePublicKey);

            Assert.Same(first, peer.CurrentKeypair);
            Assert.Null(peer.NextKeypair);
            Assert.Equal(PNetMeshWireGuardKeypairState.Current, first.State);

            table.StageNextKeypair(remotePublicKey, second);
            table.PromoteNextKeypair(remotePublicKey);

            Assert.Same(first, peer.PreviousKeypair);
            Assert.Same(second, peer.CurrentKeypair);
            Assert.Equal(PNetMeshWireGuardKeypairState.Previous, first.State);
            Assert.Equal(PNetMeshWireGuardKeypairState.Current, second.State);
            Assert.True(table.TryGetByReceiverIndex(10, out _, out var previousLookup));
            Assert.Same(first, previousLookup);
            Assert.True(table.TryGetByReceiverIndex(11, out _, out var currentLookup));
            Assert.Same(second, currentLookup);

            table.RemoveExpiredKeypairs(createdAt.Add(PNetMeshWireGuardLifecycle.RejectAfterTime));

            Assert.False(table.TryGetByReceiverIndex(10, out _, out _));
            Assert.True(table.TryGetByReceiverIndex(11, out _, out currentLookup));
            Assert.Same(second, currentLookup);
        }

        [Fact]
        public void peer_table_matches_sliced_public_key_spans()
        {
            var table = new PNetMeshWireGuardPeerTable();
            var remotePublicKey = Enumerable.Range(0, 32).Select(i => (byte)(0x60 + i)).ToArray();
            var paddedPublicKey = new byte[remotePublicKey.Length + 2];
            remotePublicKey.CopyTo(paddedPublicKey, 1);

            var peer = table.GetOrAdd(remotePublicKey);
            var slicedPeer = table.GetOrAdd(paddedPublicKey.AsSpan(1, remotePublicKey.Length));

            Assert.Same(peer, slicedPeer);
            Assert.Equal(remotePublicKey, slicedPeer.PublicKey);
        }

        [Fact]
        public void keypair_tracks_timers_counters_and_replay_window()
        {
            var createdAt = DateTimeOffset.UnixEpoch;
            var keypair = new PNetMeshWireGuardKeypair(10, 20, PNetMeshWireGuardKeypairRole.Responder, createdAt);

            Assert.False(keypair.ShouldRekey(createdAt.Add(PNetMeshWireGuardLifecycle.RekeyAfterTime).AddTicks(-1)));
            Assert.True(keypair.ShouldRekey(createdAt.Add(PNetMeshWireGuardLifecycle.RekeyAfterTime)));
            Assert.False(keypair.ShouldReject(createdAt.Add(PNetMeshWireGuardLifecycle.RejectAfterTime).AddTicks(-1)));
            Assert.True(keypair.ShouldReject(createdAt.Add(PNetMeshWireGuardLifecycle.RejectAfterTime)));

            Assert.True(keypair.TryAddReceivedCounter(7));
            Assert.False(keypair.TryAddReceivedCounter(7));
            Assert.Equal(7ul, keypair.LastReceivedCounter);

            keypair.RecordTransportActivity(createdAt.AddSeconds(1));
            Assert.False(keypair.ShouldSendKeepalive(createdAt.AddSeconds(1).Add(PNetMeshWireGuardLifecycle.KeepaliveTimeout).AddTicks(-1)));
            Assert.True(keypair.ShouldSendKeepalive(createdAt.AddSeconds(1).Add(PNetMeshWireGuardLifecycle.KeepaliveTimeout)));

            keypair.RecordSentCounter(PNetMeshWireGuardLifecycle.RekeyAfterMessages - 1);
            Assert.False(keypair.ShouldRekey(createdAt));
            keypair.RecordSentCounter(PNetMeshWireGuardLifecycle.RekeyAfterMessages);
            Assert.True(keypair.ShouldRekey(createdAt));

            keypair.RecordReceivedCounter(PNetMeshWireGuardLifecycle.RejectAfterMessages);
            Assert.True(keypair.ShouldReject(createdAt));
        }

        [Fact]
        public void wireguard_handshake_registers_current_keypair_for_receiver_index_lookup()
        {
            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiatorStatic = KeyPair.Generate();
            using var responderStatic = KeyPair.Generate();

            var initiatorProtocol = new PNetMeshProtocol(
                initiatorStatic.PrivateKey,
                initiatorStatic.PublicKey,
                psk);
            var responderProtocol = new PNetMeshProtocol(
                responderStatic.PrivateKey,
                responderStatic.PublicKey,
                psk);

            using var initiator = initiatorProtocol.CreateInitiator(1, responderStatic.PublicKey);
            using var responder = responderProtocol.CreateResponder(2);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responderTransport));
            Assert.True(initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiatorTransport));

            Assert.True(initiatorProtocol.WireGuardPeers.TryGetByReceiverIndex(initiatorTransport.SenderIndex, out var initiatorPeer, out var initiatorKeypair));
            Assert.True(responderStatic.PublicKey.AsSpan().SequenceEqual(initiatorPeer.PublicKey));
            Assert.Equal(PNetMeshWireGuardKeypairState.Current, initiatorKeypair.State);
            Assert.Equal(PNetMeshWireGuardKeypairRole.Initiator, initiatorKeypair.Role);
            Assert.Equal(initiatorTransport.SenderIndex, initiatorKeypair.LocalReceiverIndex);
            Assert.Equal(initiatorTransport.ReceiverIndex, initiatorKeypair.RemoteReceiverIndex);
            Assert.Same(initiatorKeypair, initiatorTransport.WireGuardKeypair);

            Assert.True(responderProtocol.WireGuardPeers.TryGetByReceiverIndex(responderTransport.SenderIndex, out var responderPeer, out var responderKeypair));
            Assert.True(initiatorStatic.PublicKey.AsSpan().SequenceEqual(responderPeer.PublicKey));
            Assert.Equal(PNetMeshWireGuardKeypairState.Current, responderKeypair.State);
            Assert.Equal(PNetMeshWireGuardKeypairRole.Responder, responderKeypair.Role);
            Assert.Equal(responderTransport.SenderIndex, responderKeypair.LocalReceiverIndex);
            Assert.Equal(responderTransport.ReceiverIndex, responderKeypair.RemoteReceiverIndex);
            Assert.Same(responderKeypair, responderTransport.WireGuardKeypair);

            var payload = new byte[] { 1, 2, 3 };
            initiatorTransport.WriteMessage(payload, buffer1, out bytesWritten, out var sentCounter);
            Assert.Equal(sentCounter, initiatorKeypair.LastSentCounter);

            Assert.True(responderTransport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out _, out var receivedCounter));
            Assert.Equal(receivedCounter, responderKeypair.LastReceivedCounter);
        }

        [Fact]
        public void wireguard_transport_write_rejects_keypair_after_rekey_message_threshold()
        {
            var buffer1 = new byte[4098];
            var buffer2 = new byte[4098];
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiatorStatic = KeyPair.Generate();
            using var responderStatic = KeyPair.Generate();

            var initiatorProtocol = new PNetMeshProtocol(
                initiatorStatic.PrivateKey,
                initiatorStatic.PublicKey,
                psk);
            var responderProtocol = new PNetMeshProtocol(
                responderStatic.PrivateKey,
                responderStatic.PublicKey,
                psk);

            using var initiator = initiatorProtocol.CreateInitiator(1, responderStatic.PublicKey);
            using var responder = responderProtocol.CreateResponder(2);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(buffer1.AsSpan(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responderTransport));
            using (responderTransport)
            {
                Assert.True(initiator.TryReadResponseMessage(buffer2.AsSpan(0, bytesWritten), out var initiatorTransport));
                using (initiatorTransport)
                {
                    Assert.NotNull(initiatorTransport.WireGuardKeypair);
                    initiatorTransport.WireGuardKeypair.RecordSentCounter(PNetMeshWireGuardLifecycle.RekeyAfterMessages);

                    Assert.Throws<InvalidOperationException>(
                        () => initiatorTransport.WriteMessage(new byte[] { 1, 2, 3 }, buffer1, out _, out _));
                }
            }
        }

        [Fact]
        public void wireguard_transport_read_rejects_keypair_after_reject_message_threshold()
        {
            var buffer1 = new byte[4098];
            var buffer2 = new byte[4098];
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiatorStatic = KeyPair.Generate();
            using var responderStatic = KeyPair.Generate();

            var initiatorProtocol = new PNetMeshProtocol(
                initiatorStatic.PrivateKey,
                initiatorStatic.PublicKey,
                psk);
            var responderProtocol = new PNetMeshProtocol(
                responderStatic.PrivateKey,
                responderStatic.PublicKey,
                psk);

            using var initiator = initiatorProtocol.CreateInitiator(1, responderStatic.PublicKey);
            using var responder = responderProtocol.CreateResponder(2);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(buffer1.AsSpan(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responderTransport));
            using (responderTransport)
            {
                Assert.True(initiator.TryReadResponseMessage(buffer2.AsSpan(0, bytesWritten), out var initiatorTransport));
                using (initiatorTransport)
                {
                    initiatorTransport.WriteMessage(new byte[] { 1, 2, 3 }, buffer1, out bytesWritten, out _);

                    Assert.NotNull(responderTransport.WireGuardKeypair);
                    responderTransport.WireGuardKeypair.RecordReceivedCounter(PNetMeshWireGuardLifecycle.RejectAfterMessages);

                    Assert.False(responderTransport.TryReadMessage(buffer1.AsSpan(0, bytesWritten), buffer2, out _, out _));
                }
            }
        }
    }
}
