using KeyPair = PNet.Mesh.PNetMeshKeyPair;
using System.Net;
using System.Threading.Channels;

namespace PNet.Mesh.Benchmarks;

internal static class BenchmarkProtocolHarness
{
    public const int MaxPayloadSize = 1420;
    public const int BufferSize = 2048;

    public static byte[] CreatePayload(int payloadSize)
    {
        if (payloadSize <= 0 || payloadSize > MaxPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(payloadSize), $"Payload size must be between 1 and {MaxPayloadSize} bytes.");

        var payload = new byte[payloadSize];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i % 251);
        return payload;
    }

    public static EstablishedTransportPair CreateEstablishedTransports()
    {
        var psk = new byte[32];
        for (var i = 0; i < psk.Length; i++)
            psk[i] = (byte)(0x80 + i);

        var initiatorStatic = KeyPair.Generate();
        var responderStatic = KeyPair.Generate();

        var initiatorProtocol = new PNetMeshProtocol(
            initiatorStatic.PrivateKey,
            initiatorStatic.PublicKey,
            psk);
        var responderProtocol = new PNetMeshProtocol(
            responderStatic.PrivateKey,
            responderStatic.PublicKey,
            psk);

        var initiator = initiatorProtocol.CreateInitiator(1, responderStatic.PublicKey);
        var responder = responderProtocol.CreateResponder(2);

        Span<byte> initiationBuffer = stackalloc byte[256];
        Span<byte> responseBuffer = stackalloc byte[128];

        initiator.WriteInitiationMessage(initiationBuffer, out var bytesWritten);
        if (!responder.TryReadInitiationMessage(initiationBuffer[..bytesWritten]))
            throw new InvalidOperationException("Responder rejected handshake initiation.");

        if (!responder.TryWriteResponseMessage(responseBuffer, out bytesWritten, out var responderTransport))
            throw new InvalidOperationException("Responder did not write handshake response.");

        if (!initiator.TryReadResponseMessage(responseBuffer[..bytesWritten], out var initiatorTransport))
            throw new InvalidOperationException("Initiator rejected handshake response.");

        return new EstablishedTransportPair(
            initiatorTransport,
            responderTransport,
            initiator,
            responder,
            initiatorStatic,
            responderStatic);
    }

    public static SessionPair CreateOpenSessionPair()
    {
        var psk = new byte[32];
        for (var i = 0; i < psk.Length; i++)
            psk[i] = (byte)(0x40 + i);

        var senderStatic = KeyPair.Generate();
        var receiverStatic = KeyPair.Generate();
        var senderProtocol = new PNetMeshProtocol(senderStatic.PrivateKey, senderStatic.PublicKey, psk);
        var receiverProtocol = new PNetMeshProtocol(receiverStatic.PrivateKey, receiverStatic.PublicKey, psk);
        var senderOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
        var receiverOutbound = Channel.CreateUnbounded<PNetMeshOutboundMessages.Message>();
        var senderInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var receiverInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var senderControl = Channel.CreateUnbounded<PNetMeshChannelCommands.Command>();
        var receiverControl = Channel.CreateUnbounded<PNetMeshChannelCommands.Command>();
        var sender = new PNetMeshSession(senderProtocol, senderOutbound.Writer)
        {
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 30001),
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 30002)
        };
        var receiver = new PNetMeshSession(receiverProtocol, receiverOutbound.Writer)
        {
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 30002),
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 30001)
        };
        sender.AttachTo(senderInbound.Writer, senderControl.Writer);
        receiver.AttachTo(receiverInbound.Writer, receiverControl.Writer);

        sender.WriteInitialize(1, receiverStatic.PublicKey);
        var initiation = ReadPacket(senderOutbound);
        try
        {
            if (!receiver.TryReadInitialize(2, initiation.MemoryBuffer.Span))
                throw new InvalidOperationException("Receiver rejected session initiation.");
        }
        finally
        {
            initiation.MemoryOwner?.Dispose();
        }

        receiver.WriteResponse();
        var response = ReadPacket(receiverOutbound);
        try
        {
            if (!sender.TryReadResponse(response.MemoryBuffer.Span))
                throw new InvalidOperationException("Sender rejected session response.");
        }
        finally
        {
            response.MemoryOwner?.Dispose();
        }

        return new SessionPair(
            sender,
            receiver,
            senderOutbound,
            receiverOutbound,
            senderInbound,
            receiverInbound,
            senderControl,
            receiverControl,
            senderStatic,
            receiverStatic);
    }

    public static PNetMeshOutboundMessages.Packet ReadPacket(Channel<PNetMeshOutboundMessages.Message> channel)
    {
        if (!channel.Reader.TryRead(out var message))
            throw new InvalidOperationException("Expected outbound packet.");

        return message as PNetMeshOutboundMessages.Packet
            ?? throw new InvalidOperationException("Expected packet message.");
    }
}

internal sealed class EstablishedTransportPair : IDisposable
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
            disposable.Dispose();
    }
}

internal sealed class SessionPair : IDisposable
{
    readonly IDisposable[] _disposables;

    public SessionPair(
        PNetMeshSession sender,
        PNetMeshSession receiver,
        Channel<PNetMeshOutboundMessages.Message> senderOutbound,
        Channel<PNetMeshOutboundMessages.Message> receiverOutbound,
        Channel<ReadOnlyMemory<byte>> senderInbound,
        Channel<ReadOnlyMemory<byte>> receiverInbound,
        Channel<PNetMeshChannelCommands.Command> senderControl,
        Channel<PNetMeshChannelCommands.Command> receiverControl,
        params IDisposable[] disposables)
    {
        Sender = sender;
        Receiver = receiver;
        SenderOutbound = senderOutbound;
        ReceiverOutbound = receiverOutbound;
        SenderInbound = senderInbound;
        ReceiverInbound = receiverInbound;
        SenderControl = senderControl;
        ReceiverControl = receiverControl;
        _disposables = new IDisposable[] { sender, receiver }.Concat(disposables).ToArray();
    }

    public PNetMeshSession Sender { get; }

    public PNetMeshSession Receiver { get; }

    public Channel<PNetMeshOutboundMessages.Message> SenderOutbound { get; }

    public Channel<PNetMeshOutboundMessages.Message> ReceiverOutbound { get; }

    public Channel<ReadOnlyMemory<byte>> SenderInbound { get; }

    public Channel<ReadOnlyMemory<byte>> ReceiverInbound { get; }

    public Channel<PNetMeshChannelCommands.Command> SenderControl { get; }

    public Channel<PNetMeshChannelCommands.Command> ReceiverControl { get; }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
    }
}
