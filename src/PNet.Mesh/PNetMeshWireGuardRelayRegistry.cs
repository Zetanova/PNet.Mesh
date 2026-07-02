using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Security.Cryptography;

namespace PNet.Mesh
{
    public enum PNetMeshWireGuardRelayRouteResult
    {
        Routed = 0,
        MalformedPacket,
        NoMatch,
        AmbiguousMac1Match,
        EndpointRejected,
        RateLimited
    }

    public sealed class PNetMeshWireGuardRelayOptions
    {
        public int MaxLeases { get; init; } = 1024;

        public int MaxMac1ScansPerEndpoint { get; init; } = 128;

        public TimeSpan Mac1ScanWindow { get; init; } = TimeSpan.FromSeconds(1);

        public TimeSpan ReceiverIndexTtl { get; init; } = TimeSpan.FromMinutes(2);
    }

    public sealed class PNetMeshWireGuardRelayLease
    {
        readonly ImmutableHashSet<string> _allowedEndpointKeys;

        internal PNetMeshWireGuardRelayLease(
            byte[] publicKey,
            byte[] returnAddress,
            ImmutableArray<byte[]> returnRoute,
            DateTimeOffset expiresAt,
            ImmutableArray<EndPoint> allowedRemoteEndpoints)
        {
            PublicKey = publicKey;
            ReturnAddress = returnAddress;
            ReturnRoute = returnRoute;
            ExpiresAt = expiresAt;
            AllowedRemoteEndpoints = allowedRemoteEndpoints;
            _allowedEndpointKeys = allowedRemoteEndpoints.IsDefaultOrEmpty
                ? ImmutableHashSet<string>.Empty
                : allowedRemoteEndpoints.Select(PNetMeshWireGuardRelayRegistry.GetEndpointKey).ToImmutableHashSet();
        }

        public ReadOnlyMemory<byte> PublicKey { get; }

        public ReadOnlyMemory<byte> ReturnAddress { get; }

        public ImmutableArray<byte[]> ReturnRoute { get; }

        public DateTimeOffset ExpiresAt { get; }

        public ImmutableArray<EndPoint> AllowedRemoteEndpoints { get; }

        internal bool AllowsEndpoint(EndPoint remoteEndPoint)
        {
            return _allowedEndpointKeys.Count == 0
                || _allowedEndpointKeys.Contains(PNetMeshWireGuardRelayRegistry.GetEndpointKey(remoteEndPoint));
        }
    }

    internal delegate bool PNetMeshWireGuardRelayMac1Matcher(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> publicKey);

    public sealed class PNetMeshWireGuardRelayRegistry
    {
        readonly PNetMeshWireGuardRelayOptions _options;
        readonly PNetMeshWireGuardRelayMac1Matcher _mac1Matcher;
        readonly ILogger _logger;
        readonly Dictionary<byte[], PNetMeshWireGuardRelayLease> _leases =
            new Dictionary<byte[], PNetMeshWireGuardRelayLease>(PNetMeshByteArrayComparer.Default);
        readonly Dictionary<(string RemoteEndpoint, uint ReceiverIndex), ReceiverIndexEntry> _receiverIndexes =
            new Dictionary<(string RemoteEndpoint, uint ReceiverIndex), ReceiverIndexEntry>();
        readonly Dictionary<string, ScanWindow> _scanWindows = new Dictionary<string, ScanWindow>(StringComparer.Ordinal);

        public PNetMeshWireGuardRelayRegistry(
            PNetMeshProtocol protocol,
            PNetMeshWireGuardRelayOptions options = null,
            ILogger<PNetMeshWireGuardRelayRegistry> logger = null)
            : this(options ?? new PNetMeshWireGuardRelayOptions(), CreateMac1Matcher(protocol), logger)
        {
        }

        internal PNetMeshWireGuardRelayRegistry(
            PNetMeshWireGuardRelayOptions options,
            PNetMeshWireGuardRelayMac1Matcher mac1Matcher,
            ILogger logger = null)
        {
            _options = ValidateOptions(options);
            _mac1Matcher = mac1Matcher ?? throw new ArgumentNullException(nameof(mac1Matcher));
            _logger = logger ?? NullLogger<PNetMeshWireGuardRelayRegistry>.Instance;
        }

        public int Count => _leases.Count;

        public PNetMeshWireGuardRelayLease RegisterOrRenew(
            ReadOnlySpan<byte> publicKey,
            ReadOnlySpan<byte> returnAddress,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            IEnumerable<EndPoint> allowedRemoteEndpoints = null,
            ImmutableArray<byte[]> returnRoute = default)
        {
            if (publicKey.Length != 32) throw new ArgumentOutOfRangeException(nameof(publicKey));
            if (expiresAt <= now) throw new ArgumentOutOfRangeException(nameof(expiresAt));

            PurgeExpired(now);

            var lookup = _leases.GetAlternateLookup<ReadOnlySpan<byte>>();
            var renewed = lookup.ContainsKey(publicKey);
            if (!renewed && _leases.Count >= _options.MaxLeases)
            {
                _logger.LogWarning(
                    "event=wireguard_relay_lease_rejected reason=max_leases peer_id={peerId} lease_count={leaseCount}",
                    PNetMeshDiagnosticRedactor.PublicKeyId(publicKey),
                    _leases.Count);
                throw new InvalidOperationException("maximum WireGuard relay leases reached");
            }

            var key = publicKey.ToArray();
            var lease = new PNetMeshWireGuardRelayLease(
                key,
                returnAddress.ToArray(),
                CloneRoute(returnRoute),
                expiresAt,
                CloneEndpoints(allowedRemoteEndpoints));
            _leases[key] = lease;
            _logger.LogInformation(
                "event=wireguard_relay_lease_{action} peer_id={peerId} return_address_id={returnAddressId} return_route_length={returnRouteLength} allowed_endpoint_count={allowedEndpointCount} expires_at={expiresAt:o}",
                renewed ? "renewed" : "registered",
                PNetMeshDiagnosticRedactor.PublicKeyId(publicKey),
                PNetMeshDiagnosticRedactor.AddressId(returnAddress),
                lease.ReturnRoute.IsDefault ? 0 : lease.ReturnRoute.Length,
                lease.AllowedRemoteEndpoints.IsDefault ? 0 : lease.AllowedRemoteEndpoints.Length,
                lease.ExpiresAt);
            return lease;
        }

        public bool Release(ReadOnlySpan<byte> publicKey)
        {
            if (publicKey.Length != 32) throw new ArgumentOutOfRangeException(nameof(publicKey));

            var lookup = _leases.GetAlternateLookup<ReadOnlySpan<byte>>();
            if (!lookup.Remove(publicKey, out var key, out _))
            {
                _logger.LogInformation(
                    "event=wireguard_relay_lease_release_missed peer_id={peerId}",
                    PNetMeshDiagnosticRedactor.PublicKeyId(publicKey));
                return false;
            }

            foreach (var entry in _receiverIndexes.Where(item => PNetMeshByteArrayComparer.Default.Equals(item.Value.PublicKey, key)).Select(item => item.Key).ToArray())
                _receiverIndexes.Remove(entry);
            _logger.LogInformation(
                "event=wireguard_relay_lease_released peer_id={peerId}",
                PNetMeshDiagnosticRedactor.PublicKeyId(publicKey));
            return true;
        }

        public bool TryRoute(
            ReadOnlySpan<byte> packet,
            EndPoint remoteEndPoint,
            DateTimeOffset now,
            out PNetMeshWireGuardRelayLease lease,
            out PNetMeshWireGuardRelayRouteResult result)
        {
            if (remoteEndPoint == null) throw new ArgumentNullException(nameof(remoteEndPoint));

            lease = null;
            PurgeExpired(now);

            if (!PNetMeshPacketFraming.TryReadMessageType(packet, out var messageType))
            {
                result = PNetMeshWireGuardRelayRouteResult.MalformedPacket;
                LogRouteResult(PNetMeshMessageType.Unknown, remoteEndPoint, receiverIndex: null, result, lease);
                return false;
            }

            bool routed;
            uint? receiverIndex = null;
            switch (messageType)
            {
                case PNetMeshMessageType.PacketData:
                    if (packet.Length >= PNetMeshPacketFraming.PacketDataHeaderSize)
                        receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4));
                    routed = TryRouteTransport(packet, remoteEndPoint, now, out lease, out result);
                    break;
                case PNetMeshMessageType.HandshakeInitiation:
                    if (packet.Length != PNetMeshHandshake.WireGuardInitiationMessageSize)
                    {
                        result = PNetMeshWireGuardRelayRouteResult.MalformedPacket;
                        routed = false;
                        break;
                    }
                    receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4));
                    routed = TryRouteHandshake(packet, remoteEndPoint, receiverIndex.Value, now, out lease, out result);
                    break;
                case PNetMeshMessageType.HandshakeResponse:
                    if (packet.Length != PNetMeshHandshake.ResponseMessageSize)
                    {
                        result = PNetMeshWireGuardRelayRouteResult.MalformedPacket;
                        routed = false;
                        break;
                    }
                    receiverIndex = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4));
                    routed = TryRouteHandshake(packet, remoteEndPoint, receiverIndex.Value, now, out lease, out result);
                    break;
                default:
                    result = PNetMeshWireGuardRelayRouteResult.MalformedPacket;
                    routed = false;
                    break;
            }

            LogRouteResult(messageType, remoteEndPoint, receiverIndex, result, lease);
            return routed;
        }

        bool TryRouteTransport(
            ReadOnlySpan<byte> packet,
            EndPoint remoteEndPoint,
            DateTimeOffset now,
            out PNetMeshWireGuardRelayLease lease,
            out PNetMeshWireGuardRelayRouteResult result)
        {
            lease = null;
            if (packet.Length < PNetMeshPacketFraming.PacketDataHeaderSize)
            {
                result = PNetMeshWireGuardRelayRouteResult.MalformedPacket;
                return false;
            }

            var key = (GetEndpointKey(remoteEndPoint), BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4)));
            if (!_receiverIndexes.TryGetValue(key, out var entry)
                || entry.ExpiresAt <= now
                || !_leases.TryGetValue(entry.PublicKey, out lease)
                || lease.ExpiresAt <= now)
            {
                _receiverIndexes.Remove(key);
                result = PNetMeshWireGuardRelayRouteResult.NoMatch;
                return false;
            }

            if (!lease.AllowsEndpoint(remoteEndPoint))
            {
                result = PNetMeshWireGuardRelayRouteResult.EndpointRejected;
                lease = null;
                return false;
            }

            result = PNetMeshWireGuardRelayRouteResult.Routed;
            return true;
        }

        bool TryRouteHandshake(
            ReadOnlySpan<byte> packet,
            EndPoint remoteEndPoint,
            uint receiverIndex,
            DateTimeOffset now,
            out PNetMeshWireGuardRelayLease lease,
            out PNetMeshWireGuardRelayRouteResult result)
        {
            lease = null;
            if (!TryConsumeMac1Scan(remoteEndPoint, now))
            {
                result = PNetMeshWireGuardRelayRouteResult.RateLimited;
                return false;
            }

            var endpointRejected = false;
            foreach (var candidate in _leases.Values)
            {
                if (!_mac1Matcher(packet, candidate.PublicKey.Span))
                    continue;

                if (!candidate.AllowsEndpoint(remoteEndPoint))
                {
                    endpointRejected = true;
                    continue;
                }

                if (lease is not null)
                {
                    lease = null;
                    result = PNetMeshWireGuardRelayRouteResult.AmbiguousMac1Match;
                    return false;
                }

                lease = candidate;
            }

            if (lease is null)
            {
                result = endpointRejected
                    ? PNetMeshWireGuardRelayRouteResult.EndpointRejected
                    : PNetMeshWireGuardRelayRouteResult.NoMatch;
                return false;
            }

            LearnReceiverIndex(remoteEndPoint, receiverIndex, lease, now);
            result = PNetMeshWireGuardRelayRouteResult.Routed;
            return true;
        }

        void LearnReceiverIndex(
            EndPoint remoteEndPoint,
            uint receiverIndex,
            PNetMeshWireGuardRelayLease lease,
            DateTimeOffset now)
        {
            var expiresAt = now + _options.ReceiverIndexTtl;
            if (expiresAt > lease.ExpiresAt)
                expiresAt = lease.ExpiresAt;

            _receiverIndexes[(GetEndpointKey(remoteEndPoint), receiverIndex)] = new ReceiverIndexEntry(lease.PublicKey.ToArray(), expiresAt);
            _logger.LogInformation(
                "event=wireguard_relay_receiver_index_learned endpoint_id={endpointId} receiver_index={receiverIndex} peer_id={peerId} expires_at={expiresAt:o}",
                PNetMeshDiagnosticRedactor.EndpointId(remoteEndPoint),
                receiverIndex,
                PNetMeshDiagnosticRedactor.PublicKeyId(lease.PublicKey.Span),
                expiresAt);
        }

        bool TryConsumeMac1Scan(EndPoint remoteEndPoint, DateTimeOffset now)
        {
            var endpointKey = GetEndpointKey(remoteEndPoint);
            if (!_scanWindows.TryGetValue(endpointKey, out var window)
                || now - window.Start >= _options.Mac1ScanWindow)
            {
                _scanWindows[endpointKey] = new ScanWindow(now, 1);
                return true;
            }

            if (window.Count >= _options.MaxMac1ScansPerEndpoint)
                return false;

            _scanWindows[endpointKey] = new ScanWindow(window.Start, window.Count + 1);
            return true;
        }

        void PurgeExpired(DateTimeOffset now)
        {
            foreach (var key in _leases.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToArray())
            {
                _leases.Remove(key);
                _logger.LogInformation(
                    "event=wireguard_relay_lease_expired peer_id={peerId}",
                    PNetMeshDiagnosticRedactor.PublicKeyId(key));
            }

            foreach (var key in _receiverIndexes
                .Where(item => item.Value.ExpiresAt <= now || !_leases.ContainsKey(item.Value.PublicKey))
                .Select(item => item.Key)
                .ToArray())
            {
                _receiverIndexes.Remove(key);
                _logger.LogInformation(
                    "event=wireguard_relay_receiver_index_expired endpoint_id={endpointId} receiver_index={receiverIndex}",
                    PNetMeshDiagnosticRedactor.EndpointKeyId(key.RemoteEndpoint),
                    key.ReceiverIndex);
            }
        }

        void LogRouteResult(
            PNetMeshMessageType messageType,
            EndPoint remoteEndPoint,
            uint? receiverIndex,
            PNetMeshWireGuardRelayRouteResult result,
            PNetMeshWireGuardRelayLease lease)
        {
            var eventName = messageType == PNetMeshMessageType.PacketData
                ? "wireguard_relay_fast_path"
                : "wireguard_relay_demux";
            _logger.LogDebug(
                "event={eventName} result={result} message_type={messageType} endpoint_id={endpointId} receiver_index={receiverIndex} peer_id={peerId}",
                eventName,
                result,
                messageType,
                PNetMeshDiagnosticRedactor.EndpointId(remoteEndPoint),
                receiverIndex,
                lease is null ? "peer:none" : PNetMeshDiagnosticRedactor.PublicKeyId(lease.PublicKey.Span));
        }

        static PNetMeshWireGuardRelayMac1Matcher CreateMac1Matcher(PNetMeshProtocol protocol)
        {
            if (protocol == null) throw new ArgumentNullException(nameof(protocol));

            return (packet, publicKey) =>
            {
                if (publicKey.Length != 32)
                    return false;

                var mac1Offset = GetMac1Offset(packet);
                if (mac1Offset < 0)
                    return false;

                Span<byte> expected = stackalloc byte[16];
                protocol.GetPacketMac(publicKey, packet[..mac1Offset], expected);
                return CryptographicOperations.FixedTimeEquals(expected, packet.Slice(mac1Offset, 16));
            };
        }

        static int GetMac1Offset(ReadOnlySpan<byte> packet)
        {
            if (!PNetMeshPacketFraming.TryReadMessageType(packet, out var messageType))
                return -1;

            return messageType switch
            {
                PNetMeshMessageType.HandshakeInitiation when packet.Length == PNetMeshHandshake.WireGuardInitiationMessageSize
                    => PNetMeshHandshake.WireGuardInitiationMessageSize - 32,
                PNetMeshMessageType.HandshakeResponse when packet.Length == PNetMeshHandshake.ResponseMessageSize
                    => PNetMeshHandshake.ResponseMessageSize - 32,
                _ => -1
            };
        }

        static PNetMeshWireGuardRelayOptions ValidateOptions(PNetMeshWireGuardRelayOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.MaxLeases <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxLeases));
            if (options.MaxMac1ScansPerEndpoint <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxMac1ScansPerEndpoint));
            if (options.Mac1ScanWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.Mac1ScanWindow));
            if (options.ReceiverIndexTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.ReceiverIndexTtl));
            return options;
        }

        static ImmutableArray<byte[]> CloneRoute(ImmutableArray<byte[]> route)
        {
            if (route.IsDefaultOrEmpty)
                return ImmutableArray<byte[]>.Empty;

            var builder = ImmutableArray.CreateBuilder<byte[]>(route.Length);
            foreach (var address in route)
                builder.Add(address?.ToArray() ?? Array.Empty<byte>());
            return builder.ToImmutable();
        }

        static ImmutableArray<EndPoint> CloneEndpoints(IEnumerable<EndPoint> endpoints)
        {
            if (endpoints is null)
                return ImmutableArray<EndPoint>.Empty;

            return endpoints.Select(endpoint => endpoint ?? throw new ArgumentNullException(nameof(endpoints))).ToImmutableArray();
        }

        internal static string GetEndpointKey(EndPoint endPoint)
        {
            if (endPoint == null) throw new ArgumentNullException(nameof(endPoint));
            return endPoint switch
            {
                IPEndPoint ip => $"{ip.Address}|{ip.Port}",
                DnsEndPoint dns => $"{dns.Host}|{dns.Port}",
                _ => endPoint.ToString()
            };
        }

        readonly struct ReceiverIndexEntry
        {
            public ReceiverIndexEntry(byte[] publicKey, DateTimeOffset expiresAt)
            {
                PublicKey = publicKey;
                ExpiresAt = expiresAt;
            }

            public byte[] PublicKey { get; }

            public DateTimeOffset ExpiresAt { get; }
        }

        readonly struct ScanWindow
        {
            public ScanWindow(DateTimeOffset start, int count)
            {
                Start = start;
                Count = count;
            }

            public DateTimeOffset Start { get; }

            public int Count { get; }
        }
    }
}
