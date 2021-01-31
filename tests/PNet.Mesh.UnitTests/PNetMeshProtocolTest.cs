using Noise;
using PNet.Actor.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshProtocolTest
    {
        [Fact]
        public void exchange_handshake_cookieless()
        {
            var initiator_sender_index = 1u;
            var responder_sender_index = 2u;

            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(initiator_sender_index, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(responder_sender_index);

            int bytesWritten;
            bool r;

            initiator.WriteInitiationMessage(buffer1, out bytesWritten);
            Assert.Equal(PNetMeshHandshake.InitiationMessageSize, bytesWritten);

            Assert.True(responder_protocol.ValidatePacket(buffer1.Slice(0, bytesWritten)));

            r = responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten));
            Assert.True(r);
            Assert.True(responder.Timestamp > 0);

            r = responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport);
            Assert.True(r);
            Assert.Equal(PNetMeshHandshake.ResponseMessageSize, bytesWritten);
            Assert.NotNull(responder_transport);


            Assert.True(initiator_protocol.ValidatePacket(buffer2.Slice(0, bytesWritten)));
            r = initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiator_transport);
            Assert.True(r);
            Assert.NotNull(initiator_transport);

            ulong counter;

            for (int i = 0; i < 3; i++)
            {
                //initiator needs to send first payload message
                initiator_transport.WriteMessage(Encoding.UTF8.GetBytes("Hallo"), buffer1, out bytesWritten, out counter);

                r = responder_transport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out bytesWritten, out counter);
                Assert.True(r);
                Assert.Equal(5, bytesWritten);
                Assert.Equal("Hallo", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));

                //test response
                responder_transport.WriteMessage(Encoding.UTF8.GetBytes("World"), buffer1, out bytesWritten, out counter);
                r = initiator_transport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out bytesWritten, out counter);
                Assert.True(r);
                Assert.Equal(5, bytesWritten);
                Assert.Equal("World", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));
            }
        }

        [Fact]
        public void exchange_handshake_cookie()
        {
            var initiator_sender_index = 1u;
            var responder_sender_index = 2u;

            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var cookieValue = new byte[32];
            RandomNumberGenerator.Fill(cookieValue);
            var cookie = new PNetMeshCookie(cookieValue, DateTime.UtcNow.AddMinutes(2));

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            initiator_protocol.Cookie = cookie;
            responder_protocol.Cookie = cookie;

            using var initiator = initiator_protocol.CreateInitiator(initiator_sender_index, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(responder_sender_index);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.Equal(PNetMeshHandshake.InitiationMessageSize, bytesWritten);

            bool r;
            r = responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten));
            Assert.True(r);
            Assert.True(responder.Timestamp > 0);

            r = responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport);
            Assert.True(r);
            Assert.Equal(PNetMeshHandshake.ResponseMessageSize, bytesWritten);
            Assert.NotNull(responder_transport);

            r = initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiator_transport);
            Assert.True(r);
            Assert.NotNull(initiator_transport);

            ulong counter;

            //initiator needs to send first payload message
            initiator_transport.WriteMessage(Encoding.UTF8.GetBytes("Hallo"), buffer1, out bytesWritten, out counter);

            r = responder_transport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out bytesWritten, out counter);
            Assert.True(r);
            Assert.Equal(5, bytesWritten);
            Assert.Equal("Hallo", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));

            //test response
            responder_transport.WriteMessage(Encoding.UTF8.GetBytes("World"), buffer1, out bytesWritten, out counter);
            r = initiator_transport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out bytesWritten, out counter);
            Assert.True(r);
            Assert.Equal(5, bytesWritten);
            Assert.Equal("World", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));
        }

        [Fact]
        public void send_packets_outoforder()
        {
            var initiator_sender_index = 1u;
            var responder_sender_index = 2u;

            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(initiator_sender_index, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(responder_sender_index);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.Equal(PNetMeshHandshake.InitiationMessageSize, bytesWritten);

            bool r;
            r = responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten));
            Assert.True(r);
            Assert.True(responder.Timestamp > 0);

            r = responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport);
            Assert.True(r);
            Assert.Equal(PNetMeshHandshake.ResponseMessageSize, bytesWritten);
            Assert.NotNull(responder_transport);

            r = initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiator_transport);
            Assert.True(r);
            Assert.NotNull(initiator_transport);

            ulong counter;

            var messages = new List<byte[]>();

            for (int i = 0; i < 5; i++)
            {
                //initiator needs to send first payload message
                initiator_transport.WriteMessage(Encoding.UTF8.GetBytes($"Hallo {i}"), buffer1, out bytesWritten, out counter);

                messages.Add(buffer1.Slice(0, bytesWritten).ToArray());
            }

            r = responder_transport.TryReadMessage(messages.First(), buffer2, out bytesWritten, out counter);
            Assert.True(r);
            Assert.Equal(7, bytesWritten);
            Assert.Equal("Hallo 0", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));

            for (int i = messages.Count - 2; i > 0; i--)
            {
                r = responder_transport.TryReadMessage(messages[i], buffer2, out bytesWritten, out counter);
                Assert.True(r);
                Assert.Equal(7, bytesWritten);
                Assert.Equal($"Hallo {i}", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));
            }

            r = responder_transport.TryReadMessage(messages.Last(), buffer2, out bytesWritten, out counter);
            Assert.True(r);
            Assert.Equal(7, bytesWritten);
            Assert.Equal($"Hallo 4", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));
        }
    }
}
