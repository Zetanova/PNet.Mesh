using Google.Protobuf;
using System;

using MeshProtos = global::PNet.Actor.Mesh.Protos;
using EnvelopeBody = global::PNet.Actor.Mesh.Protos.ReliableEnvelope.Types.Body;
using ReliableEnvelope = global::PNet.Actor.Mesh.Protos.ReliableEnvelope;

namespace PNet.Mesh
{
    internal interface IPNetMeshReliableControlPacketHandler
    {
        bool TryHandleEnvelope(PNetMeshReliableEnvelope envelope, ulong counter, out bool bodyReceived);

        bool TryHandleBody(EnvelopeBody body, ReadOnlyMemory<byte> bodyPayload, ulong counter, out bool bodyReceived);
    }

    internal readonly struct PNetMeshReliableEnvelope
    {
        public PNetMeshReliableEnvelope(ReliableEnvelope envelope, ReadOnlyMemory<byte> bodyTail)
        {
            Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
            BodyTail = bodyTail;
        }

        public ReliableEnvelope Envelope { get; }

        public ReadOnlyMemory<byte> BodyTail { get; }
    }

    internal sealed class PNetMeshReliableControlSession : IPNetMeshFrameHandler
    {
        readonly IPNetMeshReliableControlPacketHandler _packetHandler;

        public PNetMeshReliableControlSession(IPNetMeshReliableControlPacketHandler packetHandler)
        {
            _packetHandler = packetHandler ?? throw new ArgumentNullException(nameof(packetHandler));
        }

        public bool TryHandleFrame(ReadOnlySpan<byte> frame, ulong counter, out bool bodyReceived)
        {
            return TryHandleFrame(frame, counter, out bodyReceived, out _);
        }

        public bool TryHandleFrame(
            ReadOnlySpan<byte> frame,
            ulong counter,
            out bool bodyReceived,
            out PNetMeshPayloadFrameError error)
        {
            bodyReceived = false;
            if (!PNetMeshPayloadFraming.TryClassify(frame, out var kind, out error))
                return false;
            if (kind != PNetMeshPayloadFrameKind.PNet)
                return false;
            if (!PNetMeshPayloadFraming.TryRead(frame, out var pnetFrame, out error)
                || pnetFrame.Kind != PNetMeshPayloadFrameKind.PNet)
                return false;

            if (!pnetFrame.HasExtendedHeaderSignal
                || !PNetMeshPayloadFraming.TryReadPNetFrameType(
                    pnetFrame,
                    out var frameType,
                    out var framePayload,
                    out _))
            {
                return false;
            }

            return frameType switch
            {
                PNetMeshPNetFrameType.ReliableEnvelope => TryHandleReliableEnvelope(framePayload, counter, out bodyReceived),
                PNetMeshPNetFrameType.ReliableBodies => TryHandleReliableBodies(framePayload, counter, out bodyReceived),
                PNetMeshPNetFrameType.Body => TryHandleBody(framePayload, counter, out bodyReceived),
                _ => false
            };
        }

        bool TryHandleReliableEnvelope(ReadOnlySpan<byte> framePayload, ulong counter, out bool bodyReceived)
        {
            bodyReceived = false;
            ReliableEnvelope envelope;
            try
            {
                envelope = ReliableEnvelope.Parser.ParseFrom(framePayload);
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }

            if (envelope.Bodies.Count != 0)
                return false;

            return _packetHandler.TryHandleEnvelope(
                new PNetMeshReliableEnvelope(envelope, ReadOnlyMemory<byte>.Empty),
                counter,
                out bodyReceived);
        }

        bool TryHandleReliableBodies(ReadOnlySpan<byte> framePayload, ulong counter, out bool bodyReceived)
        {
            bodyReceived = false;
            if (!TryParseReliableBodies(framePayload, out var envelope))
                return false;

            return _packetHandler.TryHandleEnvelope(envelope, counter, out bodyReceived);
        }

        bool TryHandleBody(ReadOnlySpan<byte> framePayload, ulong counter, out bool bodyReceived)
        {
            bodyReceived = false;
            if (!TryParseBody(framePayload, out var body, out var bodyPayload))
                return false;

            return _packetHandler.TryHandleBody(body, bodyPayload, counter, out bodyReceived);
        }

        static bool TryParseReliableBodies(ReadOnlySpan<byte> payload, out PNetMeshReliableEnvelope envelope)
        {
            envelope = default;

            int envelopeLength;
            int envelopeLengthBytes;
            try
            {
                envelopeLength = PNetMeshUtils.ReadVarint32(payload, out envelopeLengthBytes);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (envelopeLength < 0 || payload.Length - envelopeLengthBytes < envelopeLength)
                return false;

            ReliableEnvelope parsedEnvelope;
            try
            {
                parsedEnvelope = ReliableEnvelope.Parser.ParseFrom(payload.Slice(envelopeLengthBytes, envelopeLength));
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }

            var bodyTail = payload[(envelopeLengthBytes + envelopeLength)..];
            if (!TryValidateBodyTail(parsedEnvelope, bodyTail.Length))
                return false;

            envelope = new PNetMeshReliableEnvelope(
                parsedEnvelope,
                bodyTail.IsEmpty ? ReadOnlyMemory<byte>.Empty : bodyTail.ToArray());
            return true;
        }

        static bool TryParseBody(
            ReadOnlySpan<byte> payload,
            out EnvelopeBody body,
            out ReadOnlyMemory<byte> bodyPayload)
        {
            body = default!;
            bodyPayload = ReadOnlyMemory<byte>.Empty;

            int bodyLength;
            int bodyLengthBytes;
            try
            {
                bodyLength = PNetMeshUtils.ReadVarint32(payload, out bodyLengthBytes);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (bodyLength < 0 || payload.Length - bodyLengthBytes < bodyLength)
                return false;

            try
            {
                body = EnvelopeBody.Parser.ParseFrom(payload.Slice(bodyLengthBytes, bodyLength));
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }

            var tail = payload[(bodyLengthBytes + bodyLength)..];
            if (!TryValidateBodyTail(body, tail.Length))
                return false;

            bodyPayload = tail.IsEmpty ? ReadOnlyMemory<byte>.Empty : tail.ToArray();
            return true;
        }

        static bool TryValidateBodyTail(ReliableEnvelope envelope, int bodyTailLength)
        {
            long expectedBodyTailLength = 0;
            foreach (var body in envelope.Bodies)
            {
                if (!TryAddBodySize(body, ref expectedBodyTailLength))
                    return false;
            }

            return expectedBodyTailLength == bodyTailLength;
        }

        static bool TryValidateBodyTail(EnvelopeBody body, int bodyTailLength)
        {
            long expectedBodyTailLength = 0;
            return TryAddBodySize(body, ref expectedBodyTailLength)
                && expectedBodyTailLength == bodyTailLength;
        }

        static bool TryAddBodySize(EnvelopeBody? body, ref long bodyTailLength)
        {
            if (body?.Metadata is null)
                return false;

            var bodySize = body.Metadata.CompressedSize != 0
                ? body.Metadata.CompressedSize
                : body.Metadata.PayloadSize;

            bodyTailLength += bodySize;
            return bodyTailLength <= int.MaxValue;
        }
    }
}
