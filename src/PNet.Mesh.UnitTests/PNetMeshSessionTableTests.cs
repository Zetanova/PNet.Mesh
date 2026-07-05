using PNet.Mesh;
using System;
using System.Security.Cryptography;
using System.Threading.Channels;
using Xunit;
using KeyPair = PNet.Mesh.PNetMeshKeyPair;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshSessionTableTests
    {
        [Fact]
        public void try_get_uses_published_receiver_index_slots()
        {
            using var first = CreateSession();
            using var second = CreateSession();
            using var third = CreateSession();
            var sessions = new PNetMeshSessionTable();

            sessions.Add(1, first);
            sessions.Add(2, second);
            sessions.Add(3, third);

            Assert.True(sessions.TryGet(2, out var match));
            Assert.Same(second, match);
            Assert.False(sessions.TryGet(4, out _));
        }

        [Fact]
        public void remove_tombstones_session_without_reordering_later_indexes()
        {
            using var first = CreateSession();
            using var second = CreateSession();
            using var third = CreateSession();
            var sessions = new PNetMeshSessionTable();

            sessions.Add(1, first);
            sessions.Add(2, second);
            sessions.Add(3, third);

            Assert.True(sessions.Remove(2));

            Assert.True(sessions.TryGet(1, out var firstMatch));
            Assert.Same(first, firstMatch);
            Assert.False(sessions.TryGet(2, out _));
            Assert.True(sessions.TryGet(3, out var thirdMatch));
            Assert.Same(third, thirdMatch);
        }

        [Fact]
        public void add_grows_published_arrays_without_breaking_binary_search()
        {
            var stored = new PNetMeshSession[6];
            var sessions = new PNetMeshSessionTable(2);
            try
            {
                for (var i = 0; i < stored.Length; i++)
                {
                    stored[i] = CreateSession();
                    sessions.Add((uint)(i + 1), stored[i]);
                }

                Assert.Equal(stored.Length, sessions.PublishedCount);
                Assert.True(sessions.TryGet(6, out var match));
                Assert.Same(stored[5], match);
            }
            finally
            {
                foreach (var session in stored)
                    session?.Dispose();
            }
        }

        static PNetMeshSession CreateSession()
        {
            using var key = KeyPair.Generate();
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);
            var protocol = new PNetMeshProtocol(key.PrivateKey, key.PublicKey, psk);
            var outbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
            return new PNetMeshSession(protocol, outbound.Writer);
        }
    }
}
