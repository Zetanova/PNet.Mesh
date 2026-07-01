using Noise;
using PNet.Mesh;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshProtocolTest
    {
        const int PacketDataHeaderBytes = 16;
        const int AeadTagBytes = 16;
        const int PacketDataBaseOverheadBytes = PacketDataHeaderBytes + AeadTagBytes;

        [Fact]
        public void valid_peers_complete_noise_ikpsk2_handshake_and_exchange_payloads_over_derived_transports_regression()
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

            var r = responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten));
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
            var initiatorPayload = Encoding.UTF8.GetBytes("initiator payload");
            var responderPayload = Encoding.UTF8.GetBytes("responder payload");

            initiator_transport.WriteMessage(initiatorPayload, buffer1, out bytesWritten, out counter);
            r = responder_transport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out bytesWritten, out counter);
            Assert.True(r);
            Assert.Equal(initiatorPayload.Length, bytesWritten);
            Assert.Equal("initiator payload", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));

            responder_transport.WriteMessage(responderPayload, buffer1, out bytesWritten, out counter);
            r = initiator_transport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out bytesWritten, out counter);
            Assert.True(r);
            Assert.Equal(responderPayload.Length, bytesWritten);
            Assert.Equal("responder payload", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1455)]
        [InlineData(1456)]
        public void packet_data_size_includes_32_byte_base_overhead_excluding_padding(int payloadLength)
        {
            using var transports = CreateEstablishedTransports();
            var payload = CreatePayload(payloadLength);
            var expectedPadding = CalculatePacketDataPadding(payloadLength);
            var expectedPacketLength = payloadLength + expectedPadding + PacketDataBaseOverheadBytes;
            var packet = new byte[expectedPacketLength];
            packet.AsSpan().Fill(0xff);

            transports.Initiator.WriteMessage(payload, packet, out var bytesWritten, out var counter);

            Assert.Equal(expectedPacketLength, bytesWritten);
            Assert.Equal(PacketDataBaseOverheadBytes, bytesWritten - payloadLength - expectedPadding);
            Assert.Equal(0ul, counter);
            Assert.Equal((byte)PNetMeshMessageType.PacketData, packet[0]);
            Assert.Equal(new byte[] { 0, 0, 0 }, packet.AsSpan(1, 3).ToArray());
            Assert.Equal(transports.Initiator.ReceiverIndex, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(counter, BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(8, 8)));

            var plaintext = new byte[expectedPacketLength];
            Assert.True(transports.Responder.TryReadMessage(packet, plaintext, out var bytesRead, out var readCounter));
            Assert.Equal(counter, readCounter);
            Assert.Equal(payloadLength, bytesRead);
            Assert.True(payload.AsSpan().SequenceEqual(plaintext.AsSpan(0, bytesRead)));
        }

        [Fact]
        public void packet_data_write_requires_buffer_for_payload_padding_header_and_tag_boundary()
        {
            using var transports = CreateEstablishedTransports();
            var payload = CreatePayload(1456);
            var expectedPadding = CalculatePacketDataPadding(payload.Length);
            var expectedPacketLength = payload.Length + expectedPadding + PacketDataBaseOverheadBytes;

            var tooSmall = new byte[expectedPacketLength - 1];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                transports.Initiator.WriteMessage(payload, tooSmall, out _, out _));

            var exact = new byte[expectedPacketLength];
            transports.Initiator.WriteMessage(payload, exact, out var bytesWritten, out _);

            Assert.Equal(expectedPacketLength, bytesWritten);
        }

        [Fact]
        public void validate_packet_rejects_corrupted_handshake_initiation_mac_regression()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);

            Span<byte> buffer = new byte[4098];
            initiator.WriteInitiationMessage(buffer, out var bytesWritten);
            var initiation = buffer.Slice(0, bytesWritten).ToArray();

            Assert.True(responder_protocol.ValidatePacket(initiation));

            initiation[PNetMeshHandshake.InitiationMessageSize - 32] ^= 0xff;

            Assert.False(responder_protocol.ValidatePacket(initiation));
        }

        [Fact]
        public void validate_packet_rejects_corrupted_handshake_response_mac_regression()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(2);

            Span<byte> initiationBuffer = new byte[4098];
            Span<byte> responseBuffer = new byte[4098];
            initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(initiationBuffer.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responder_transport));
            Assert.NotNull(responder_transport);

            var response = responseBuffer.Slice(0, bytesWritten).ToArray();
            Assert.True(initiator_protocol.ValidatePacket(response));

            response[PNetMeshHandshake.ResponseMessageSize - 32] ^= 0xff;

            Assert.False(initiator_protocol.ValidatePacket(response));
        }

        [Fact]
        public void try_read_response_rejects_wrong_psk_regression()
        {
            var initiatorPsk = new byte[32];
            var responderPsk = new byte[32];
            RandomNumberGenerator.Fill(initiatorPsk);
            RandomNumberGenerator.Fill(responderPsk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, initiatorPsk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, responderPsk);

            using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(2);

            Span<byte> buffer = new byte[4098];
            initiator.WriteInitiationMessage(buffer, out var bytesWritten);

            Assert.True(responder_protocol.ValidatePacket(buffer.Slice(0, bytesWritten)));
            Assert.True(responder.TryReadInitiationMessage(buffer.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer, out bytesWritten, out var responder_transport));
            Assert.NotNull(responder_transport);
            Assert.False(initiator.TryReadResponseMessage(buffer.Slice(0, bytesWritten), out var initiator_transport));
            Assert.Null(initiator_transport);
        }

        [Fact]
        public void validate_packet_rejects_wrong_responder_key_regression()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var expected_responder_static = KeyPair.Generate();
            using var wrong_responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var expected_responder_protocol = new PNetMeshProtocol(expected_responder_static.PrivateKey, expected_responder_static.PublicKey, psk);
            var wrong_responder_protocol = new PNetMeshProtocol(wrong_responder_static.PrivateKey, wrong_responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(1, expected_responder_static.PublicKey);

            Span<byte> buffer = new byte[4098];
            initiator.WriteInitiationMessage(buffer, out var bytesWritten);

            Assert.True(expected_responder_protocol.ValidatePacket(buffer.Slice(0, bytesWritten)));
            Assert.False(wrong_responder_protocol.ValidatePacket(buffer.Slice(0, bytesWritten)));
        }

        [Fact]
        public void try_read_message_rejects_tampered_payload_without_plaintext_regression()
        {
            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(2);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport));
            Assert.True(initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiator_transport));

            initiator_transport.WriteMessage(Encoding.UTF8.GetBytes("tamper"), buffer1, out bytesWritten, out _);
            var packet = buffer1.Slice(0, bytesWritten).ToArray();
            packet[packet.Length - 1] ^= 0xff;

            Assert.False(responder_transport.TryReadMessage(packet, buffer2, out bytesWritten, out _));
            Assert.Equal(0, bytesWritten);
        }

        [Fact]
        public void try_read_message_does_not_consume_counter_for_tampered_payload_regression()
        {
            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(2);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport));
            Assert.True(initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiator_transport));

            initiator_transport.WriteMessage(Encoding.UTF8.GetBytes("authentic"), buffer1, out bytesWritten, out _);
            var authentic = buffer1.Slice(0, bytesWritten).ToArray();
            var tampered = authentic.ToArray();
            tampered[tampered.Length - 1] ^= 0xff;

            Assert.False(responder_transport.TryReadMessage(tampered, buffer2, out bytesWritten, out _));
            Assert.Equal(0, bytesWritten);

            Assert.True(responder_transport.TryReadMessage(authentic, buffer2, out bytesWritten, out _));
            Assert.Equal("authentic", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));
        }

        [Fact]
        public void try_read_message_rejects_unknown_receiver_index_without_plaintext_regression()
        {
            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(2);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport));
            Assert.True(initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiator_transport));

            initiator_transport.WriteMessage(Encoding.UTF8.GetBytes("unknown"), buffer1, out bytesWritten, out _);
            var packet = buffer1.Slice(0, bytesWritten).ToArray();
            packet[4] = 0xfe;
            packet[5] = 0xca;
            packet[6] = 0xad;
            packet[7] = 0xde;

            Assert.False(responder_transport.TryReadMessage(packet, buffer2, out bytesWritten, out _));
            Assert.Equal(0, bytesWritten);
        }

        [Fact]
        public void try_read_message_rejects_replayed_packet_without_plaintext_regression()
        {
            Span<byte> buffer1 = new byte[4098];
            Span<byte> buffer2 = new byte[4098];

            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk);

            using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);
            using var responder = responder_protocol.CreateResponder(2);

            initiator.WriteInitiationMessage(buffer1, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport));
            Assert.True(initiator.TryReadResponseMessage(buffer2.Slice(0, bytesWritten), out var initiator_transport));

            initiator_transport.WriteMessage(Encoding.UTF8.GetBytes("first"), buffer1, out bytesWritten, out var writtenCounter);
            Assert.Equal(0ul, writtenCounter);
            Assert.True(responder_transport.TryReadMessage(buffer1.Slice(0, bytesWritten), buffer2, out bytesWritten, out var readCounter));
            Assert.Equal(0ul, readCounter);

            initiator_transport.WriteMessage(Encoding.UTF8.GetBytes("second"), buffer1, out bytesWritten, out writtenCounter);
            Assert.Equal(1ul, writtenCounter);
            var replayed = buffer1.Slice(0, bytesWritten).ToArray();

            Assert.True(responder_transport.TryReadMessage(replayed, buffer2, out bytesWritten, out readCounter));
            Assert.Equal(1ul, readCounter);
            Assert.Equal("second", Encoding.UTF8.GetString(buffer2.Slice(0, bytesWritten)));

            buffer2.Slice(0, 32).Fill(0x5a);

            Assert.False(responder_transport.TryReadMessage(replayed, buffer2, out bytesWritten, out readCounter));
            Assert.Equal(0, bytesWritten);
            Assert.Equal(1ul, readCounter);
            Assert.All(buffer2.Slice(0, 16).ToArray(), b => Assert.Equal((byte)0, b));
        }

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
        public void validate_packet_accepts_matching_cookie_macs_regression()
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
            Assert.True(responder_protocol.ValidatePacket(buffer1.Slice(0, bytesWritten)));
            Assert.True(responder.TryReadInitiationMessage(buffer1.Slice(0, bytesWritten)));

            Assert.True(responder.TryWriteResponseMessage(buffer2, out bytesWritten, out var responder_transport));
            Assert.NotNull(responder_transport);
            Assert.True(initiator_protocol.ValidatePacket(buffer2.Slice(0, bytesWritten)));
        }

        [Fact]
        public void validate_packet_rejects_missing_or_wrong_cookie_initiation_mac_regression()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var expectedCookieValue = new byte[32];
            var wrongCookieValue = new byte[32];
            RandomNumberGenerator.Fill(expectedCookieValue);
            expectedCookieValue.CopyTo(wrongCookieValue, 0);
            wrongCookieValue[0] ^= 0xff;

            var expectedCookie = new PNetMeshCookie(expectedCookieValue, DateTime.UtcNow.AddMinutes(2));
            var wrongCookie = new PNetMeshCookie(wrongCookieValue, DateTime.UtcNow.AddMinutes(2));

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk);
            var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk)
            {
                Cookie = expectedCookie
            };

            Span<byte> buffer = new byte[4098];
            using (var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey))
            {
                initiator.WriteInitiationMessage(buffer, out var bytesWritten);
                Assert.False(responder_protocol.ValidatePacket(buffer.Slice(0, bytesWritten)));
            }

            initiator_protocol.Cookie = wrongCookie;
            using (var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey))
            {
                initiator.WriteInitiationMessage(buffer, out var bytesWritten);
                Assert.False(responder_protocol.ValidatePacket(buffer.Slice(0, bytesWritten)));
            }
        }

        [Fact]
        public void validate_packet_rejects_missing_or_wrong_cookie_response_mac_regression()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var expectedCookieValue = new byte[32];
            var wrongCookieValue = new byte[32];
            RandomNumberGenerator.Fill(expectedCookieValue);
            expectedCookieValue.CopyTo(wrongCookieValue, 0);
            wrongCookieValue[0] ^= 0xff;

            var expectedCookie = new PNetMeshCookie(expectedCookieValue, DateTime.UtcNow.AddMinutes(2));
            var wrongCookie = new PNetMeshCookie(wrongCookieValue, DateTime.UtcNow.AddMinutes(2));

            using var initiator_static = KeyPair.Generate();
            using var responder_static = KeyPair.Generate();

            Assert.False(WriteResponseWithoutMatchingCookie(PNetMeshCookie.Empty));
            Assert.False(WriteResponseWithoutMatchingCookie(wrongCookie));

            bool WriteResponseWithoutMatchingCookie(PNetMeshCookie responderCookie)
            {
                var initiator_protocol = new PNetMeshProtocol(initiator_static.PrivateKey, initiator_static.PublicKey, psk)
                {
                    Cookie = expectedCookie
                };
                var responder_protocol = new PNetMeshProtocol(responder_static.PrivateKey, responder_static.PublicKey, psk)
                {
                    Cookie = responderCookie
                };

                Span<byte> initiationBuffer = new byte[4098];
                Span<byte> responseBuffer = new byte[4098];

                using var initiator = initiator_protocol.CreateInitiator(1, responder_static.PublicKey);
                using var responder = responder_protocol.CreateResponder(2);

                initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
                Assert.True(responder.TryReadInitiationMessage(initiationBuffer.Slice(0, bytesWritten)));
                Assert.True(responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responder_transport));
                Assert.NotNull(responder_transport);

                return initiator_protocol.ValidatePacket(responseBuffer.Slice(0, bytesWritten));
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

        static EstablishedTransportPair CreateEstablishedTransports()
        {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var initiatorStatic = KeyPair.Generate();
            var responderStatic = KeyPair.Generate();

            var initiatorProtocol = new PNetMeshProtocol(initiatorStatic.PrivateKey, initiatorStatic.PublicKey, psk);
            var responderProtocol = new PNetMeshProtocol(responderStatic.PrivateKey, responderStatic.PublicKey, psk);

            var initiator = initiatorProtocol.CreateInitiator(1, responderStatic.PublicKey);
            var responder = responderProtocol.CreateResponder(2);

            Span<byte> initiationBuffer = new byte[PNetMeshHandshake.InitiationMessageSize];
            Span<byte> responseBuffer = new byte[PNetMeshHandshake.ResponseMessageSize];

            initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
            Assert.True(responder.TryReadInitiationMessage(initiationBuffer.Slice(0, bytesWritten)));
            Assert.True(responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responderTransport));
            Assert.True(initiator.TryReadResponseMessage(responseBuffer.Slice(0, bytesWritten), out var initiatorTransport));

            return new EstablishedTransportPair(
                initiatorTransport,
                responderTransport,
                initiator,
                responder,
                initiatorStatic,
                responderStatic);
        }

        static byte[] CreatePayload(int length)
        {
            var payload = new byte[length];

            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 251);
            }

            return payload;
        }

        static int CalculatePacketDataPadding(int payloadLength)
        {
            var remainder = payloadLength % 16;
            return remainder == 0 ? 16 : 16 - remainder;
        }

        sealed class EstablishedTransportPair : IDisposable
        {
            readonly IDisposable[] _disposables;

            public EstablishedTransportPair(
                PNetMeshTransport2 initiator,
                PNetMeshTransport2 responder,
                params IDisposable[] disposables)
            {
                Initiator = initiator;
                Responder = responder;
                _disposables = new IDisposable[] { initiator, responder }.Concat(disposables).ToArray();
            }

            public PNetMeshTransport2 Initiator { get; }

            public PNetMeshTransport2 Responder { get; }

            public void Dispose()
            {
                foreach (var disposable in _disposables)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
