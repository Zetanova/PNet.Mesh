using System;
using System.Buffers.Binary;
using System.Diagnostics;

#if PNET_MESH_PACKET_TRACE
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
#endif

namespace PNet.Mesh
{
    internal enum PNetMeshPacketTraceStage
    {
        TunReadDone = 1,
        IpParsed = 2,
        PeerRouteFound = 3,
        RawSendAttempt = 4,
        RawSendDispatched = 5,
        RawSendDropped = 6,
        EncryptStart = 7,
        EncryptDone = 8,
        UdpSendStart = 9,
        UdpSendCompletedSync = 10,
        UdpSendCompletedAsync = 11,
        UdpSendFallbackQueued = 12,
        UdpReceiveDone = 13,
        InboundDirectStart = 14,
        InboundFallbackQueued = 15,
        DecryptStart = 16,
        DecryptDone = 17,
        PlaintextIPv4Detected = 18,
        PlaintextIPv6Detected = 19,
        TunWriteStart = 20,
        TunWriteDone = 21,
        TunWriteFallbackToChannel = 22,
        QueuedTunWriteStart = 23,
        QueuedTunWriteDone = 24
    }

    internal readonly struct PNetMeshPacketTraceKey
    {
        public PNetMeshPacketTraceKey(
            byte ipVersion,
            byte icmpType,
            ushort icmpId,
            ushort icmpSequence,
            ulong sourceHigh,
            ulong sourceLow,
            ulong destinationHigh,
            ulong destinationLow)
        {
            IpVersion = ipVersion;
            IcmpType = icmpType;
            IcmpId = icmpId;
            IcmpSequence = icmpSequence;
            SourceHigh = sourceHigh;
            SourceLow = sourceLow;
            DestinationHigh = destinationHigh;
            DestinationLow = destinationLow;
        }

        public byte IpVersion { get; }

        public byte IcmpType { get; }

        public ushort IcmpId { get; }

        public ushort IcmpSequence { get; }

        public ulong SourceHigh { get; }

        public ulong SourceLow { get; }

        public ulong DestinationHigh { get; }

        public ulong DestinationLow { get; }
    }

    internal static class PNetMeshPacketTrace
    {
#if PNET_MESH_PACKET_TRACE
        [ThreadStatic]
        static long _udpReceiveTimestamp;

        [ThreadStatic]
        static long _inboundDirectStartTimestamp;

        [ThreadStatic]
        static long _decryptStartTimestamp;

        static readonly PNetMeshPacketTraceSink? Sink = PNetMeshPacketTraceSink.Create();
#endif

        [Conditional("PNET_MESH_PACKET_TRACE")]
        public static void SetUdpReceiveTimestamp(long timestamp)
        {
#if PNET_MESH_PACKET_TRACE
            _udpReceiveTimestamp = timestamp;
#endif
        }

        [Conditional("PNET_MESH_PACKET_TRACE")]
        public static void ClearUdpReceiveTimestamp()
        {
#if PNET_MESH_PACKET_TRACE
            _udpReceiveTimestamp = 0;
            _inboundDirectStartTimestamp = 0;
            _decryptStartTimestamp = 0;
#endif
        }

        [Conditional("PNET_MESH_PACKET_TRACE")]
        public static void MarkDecryptStart()
        {
#if PNET_MESH_PACKET_TRACE
            _decryptStartTimestamp = Stopwatch.GetTimestamp();
#endif
        }

        [Conditional("PNET_MESH_PACKET_TRACE")]
        public static void MarkInboundDirectStart()
        {
#if PNET_MESH_PACKET_TRACE
            _inboundDirectStartTimestamp = Stopwatch.GetTimestamp();
#endif
        }

        [Conditional("PNET_MESH_PACKET_TRACE")]
        public static void RecordPacket(
            PNetMeshPacketTraceStage stage,
            ReadOnlySpan<byte> packet,
            int byteCount,
            int result = 0)
        {
#if PNET_MESH_PACKET_TRACE
            var sink = Sink;
            if (sink is null)
                return;
            if (!TryCreateKey(packet.Slice(0, Math.Min(byteCount, packet.Length)), out var key))
                return;

            var timestamp = Stopwatch.GetTimestamp();
            if (_udpReceiveTimestamp != 0)
            {
                sink.RecordAt(_udpReceiveTimestamp, PNetMeshPacketTraceStage.UdpReceiveDone, key, byteCount, 0);
                _udpReceiveTimestamp = 0;
            }

            if (_inboundDirectStartTimestamp != 0)
            {
                sink.RecordAt(_inboundDirectStartTimestamp, PNetMeshPacketTraceStage.InboundDirectStart, key, byteCount, 0);
                _inboundDirectStartTimestamp = 0;
            }

            if (stage == PNetMeshPacketTraceStage.DecryptDone && _decryptStartTimestamp != 0)
            {
                sink.RecordAt(_decryptStartTimestamp, PNetMeshPacketTraceStage.DecryptStart, key, byteCount, 0);
                _decryptStartTimestamp = 0;
            }

            sink.RecordAt(timestamp, stage, key, byteCount, result);
#endif
        }

        [Conditional("PNET_MESH_PACKET_TRACE")]
        public static void RecordKey(
            PNetMeshPacketTraceStage stage,
            PNetMeshPacketTraceKey key,
            int byteCount,
            int result = 0)
        {
#if PNET_MESH_PACKET_TRACE
            Sink?.RecordAt(Stopwatch.GetTimestamp(), stage, key, byteCount, result);
#endif
        }

        [Conditional("PNET_MESH_PACKET_TRACE")]
        public static void Flush()
        {
#if PNET_MESH_PACKET_TRACE
            Sink?.Flush();
#endif
        }

        public static bool TryCreateKey(ReadOnlySpan<byte> packet, out PNetMeshPacketTraceKey key)
        {
            key = default;
            if (packet.Length < 1)
                return false;

            return (packet[0] >> 4) switch
            {
                4 => TryCreateIPv4Key(packet, out key),
                6 => TryCreateIPv6Key(packet, out key),
                _ => false
            };
        }

        static bool TryCreateIPv4Key(ReadOnlySpan<byte> packet, out PNetMeshPacketTraceKey key)
        {
            key = default;
            if (packet.Length < 28)
                return false;

            var headerLength = (packet[0] & 0x0f) * 4;
            if (headerLength < 20 || packet.Length < headerLength + 8)
                return false;
            if (packet[9] != 1)
                return false;

            var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
            if (totalLength < headerLength + 8 || totalLength > packet.Length)
                return false;

            var icmp = packet.Slice(headerLength, totalLength - headerLength);
            var type = icmp[0];
            if (type is not (0 or 8))
                return false;

            key = new PNetMeshPacketTraceKey(
                4,
                type,
                BinaryPrimitives.ReadUInt16BigEndian(icmp.Slice(4, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(icmp.Slice(6, 2)),
                0,
                BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(12, 4)),
                0,
                BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(16, 4)));
            return true;
        }

        static bool TryCreateIPv6Key(ReadOnlySpan<byte> packet, out PNetMeshPacketTraceKey key)
        {
            key = default;
            if (packet.Length < 48)
                return false;

            var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(4, 2));
            var totalLength = 40 + payloadLength;
            if (payloadLength < 8 || totalLength > packet.Length)
                return false;
            if (packet[6] != 58)
                return false;

            var icmp = packet.Slice(40, payloadLength);
            var type = icmp[0];
            if (type is not (128 or 129))
                return false;

            key = new PNetMeshPacketTraceKey(
                6,
                type,
                BinaryPrimitives.ReadUInt16BigEndian(icmp.Slice(4, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(icmp.Slice(6, 2)),
                BinaryPrimitives.ReadUInt64BigEndian(packet.Slice(8, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(packet.Slice(16, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(packet.Slice(24, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(packet.Slice(32, 8)));
            return true;
        }

#if PNET_MESH_PACKET_TRACE
        readonly struct PNetMeshPacketTraceEvent
        {
            public PNetMeshPacketTraceEvent(
                long sequence,
                long timestamp,
                int threadId,
                PNetMeshPacketTraceStage stage,
                PNetMeshPacketTraceKey key,
                int byteCount,
                int result)
            {
                Sequence = sequence;
                Timestamp = timestamp;
                ThreadId = threadId;
                Stage = stage;
                Key = key;
                ByteCount = byteCount;
                Result = result;
            }

            public long Sequence { get; }

            public long Timestamp { get; }

            public int ThreadId { get; }

            public PNetMeshPacketTraceStage Stage { get; }

            public PNetMeshPacketTraceKey Key { get; }

            public int ByteCount { get; }

            public int Result { get; }
        }

        sealed class PNetMeshPacketTraceSink
        {
            readonly PNetMeshPacketTraceEvent[] _events;
            readonly int _mask;
            readonly string _path;
            // multi-threading: packet record callers can run while process-exit or POSIX signal callbacks flush the sink.
            readonly System.Threading.Lock _gate = new();
            PosixSignalRegistration? _dumpSignal;
            PosixSignalRegistration? _terminateSignal;
            long _next = -1;
            int _flushStarted;
            static readonly ulong AddressHashSalt = CreateAddressHashSalt();

            PNetMeshPacketTraceSink(string path, int capacity)
            {
                _path = path;
                _events = new PNetMeshPacketTraceEvent[RoundUpPowerOfTwo(capacity)];
                _mask = _events.Length - 1;
            }

            public static PNetMeshPacketTraceSink? Create()
            {
                var path = Environment.GetEnvironmentVariable("PNET_MESH_PACKET_TRACE_FILE");
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                var capacity = 65536;
                var capacityText = Environment.GetEnvironmentVariable("PNET_MESH_PACKET_TRACE_CAPACITY");
                if (!string.IsNullOrWhiteSpace(capacityText))
                {
                    if (!int.TryParse(capacityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCapacity)
                        || parsedCapacity <= 0)
                    {
                        throw new InvalidOperationException("PNET_MESH_PACKET_TRACE_CAPACITY must be a positive integer.");
                    }

                    capacity = parsedCapacity;
                }

                var sink = new PNetMeshPacketTraceSink(path, capacity);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => sink.Flush();
                if (!OperatingSystem.IsWindows())
                {
                    sink._dumpSignal = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
                    {
                        context.Cancel = true;
                        sink.Flush();
                    });
                    sink._terminateSignal = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => sink.Flush());
                }

                return sink;
            }

            public void RecordAt(
                long timestamp,
                PNetMeshPacketTraceStage stage,
                PNetMeshPacketTraceKey key,
                int byteCount,
                int result)
            {
                lock (_gate)
                {
                    var sequence = ++_next;
                    _events[sequence & _mask] = new PNetMeshPacketTraceEvent(
                        sequence,
                        timestamp,
                        Environment.CurrentManagedThreadId,
                        stage,
                        key,
                        byteCount,
                        result);
                }
            }

            public void Flush()
            {
                if (Interlocked.Exchange(ref _flushStarted, 1) != 0)
                    return;

                try
                {
                    var next = -1L;
                    var eventCount = 0;
                    var snapshot = Array.Empty<PNetMeshPacketTraceEvent>();
                    lock (_gate)
                    {
                        next = _next;
                        var count = Math.Min(next + 1, _events.Length);
                        if (count > 0)
                        {
                            snapshot = new PNetMeshPacketTraceEvent[count];
                            var start = Math.Max(0, next - count + 1);
                            for (var sequence = start; sequence <= next; sequence++)
                            {
                                var item = _events[sequence & _mask];
                                if (item.Sequence != sequence)
                                    continue;

                                snapshot[eventCount++] = item;
                            }
                        }
                    }

                    var directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    using var writer = new StreamWriter(_path, append: false);
                    writer.Write("# role=");
                    writer.Write(Environment.GetEnvironmentVariable("PNET_MESH_PACKET_TRACE_ROLE") ?? string.Empty);
                    writer.Write(" pid=");
                    writer.Write(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                    writer.Write(" frequency=");
                    writer.Write(Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
                    writer.Write(" capacity=");
                    writer.Write(_events.Length.ToString(CultureInfo.InvariantCulture));
                    writer.Write(" next=");
                    writer.WriteLine(next.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine("sequence,timestamp,thread_id,stage,stage_name,ip_version,icmp_type,icmp_id,icmp_seq,source_id,destination_id,bytes,result");

                    for (var i = 0; i < eventCount; i++)
                    {
                        WriteEvent(writer, snapshot[i]);
                    }
                }
                finally
                {
                    Volatile.Write(ref _flushStarted, 0);
                }
            }

            static void WriteEvent(StreamWriter writer, PNetMeshPacketTraceEvent item)
            {
                writer.Write(item.Sequence.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(item.Timestamp.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(item.ThreadId.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(((int)item.Stage).ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(item.Stage.ToString());
                writer.Write(',');
                writer.Write(item.Key.IpVersion.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(item.Key.IcmpType.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(item.Key.IcmpId.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(item.Key.IcmpSequence.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(FormatEndpointId(item.Key, source: true));
                writer.Write(',');
                writer.Write(FormatEndpointId(item.Key, source: false));
                writer.Write(',');
                writer.Write(item.ByteCount.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.WriteLine(item.Result.ToString(CultureInfo.InvariantCulture));
            }

            static string FormatEndpointId(PNetMeshPacketTraceKey key, bool source)
            {
                return HashEndpoint(key, source).ToString("x16", CultureInfo.InvariantCulture);
            }

            static ulong HashEndpoint(PNetMeshPacketTraceKey key, bool source)
            {
                var high = source ? key.SourceHigh : key.DestinationHigh;
                var low = source ? key.SourceLow : key.DestinationLow;
                var hash = 1469598103934665603UL ^ AddressHashSalt;
                hash = MixHash(hash, key.IpVersion);
                hash = MixHash(hash, high);
                return MixHash(hash, low);
            }

            static ulong MixHash(ulong hash, ulong value)
            {
                const ulong prime = 1099511628211UL;
                for (var i = 0; i < 8; i++)
                {
                    hash ^= value & 0xff;
                    hash *= prime;
                    value >>= 8;
                }

                return hash;
            }

            static ulong CreateAddressHashSalt()
            {
                Span<byte> bytes = stackalloc byte[8];
                RandomNumberGenerator.Fill(bytes);
                return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            }

            static int RoundUpPowerOfTwo(int value)
            {
                var result = 1;
                while (result < value && result < 1 << 30)
                    result <<= 1;
                return result;
            }
        }
#endif
    }
}
