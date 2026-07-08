using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;

namespace PNet.Mesh
{
    public sealed class PNetMeshRoutingEntry
    {
        /// <summary>
        /// Peer address is BLAKE2s-256("pnet.mesh.address.v1" || static_public_key)[0..10].
        /// </summary>
        public required byte[] Address { get; init; }

        public EndPoint? EndPoint { get; set; }

        public DateTime LastSeen { get; set; }

        /// <summary>
        /// relay tracker
        /// </summary>
        public PNetMeshPacketTracker? Tracker { get; set; }
    }

    public sealed class PNetMeshRouter
    {
        readonly Dictionary<byte[], PNetMeshRoutingEntry> _entries;

        public PNetMeshRouter()
        {
            _entries = new Dictionary<byte[], PNetMeshRoutingEntry>(PNetMeshByteArrayComparer.Default);
        }

        public bool TryGetEntry(byte[] address, [NotNullWhen(true)] out PNetMeshRoutingEntry? entry)
        {
            return _entries.TryGetValue(address, out entry);
        }

        public bool TryGetEntry(ReadOnlySpan<byte> address, [NotNullWhen(true)] out PNetMeshRoutingEntry? entry)
        {
            return _entries.GetAlternateLookup<ReadOnlySpan<byte>>().TryGetValue(address, out entry);
        }

        public void SetEntry(byte[] address, EndPoint? endPoint)
        {
            if (address?.Length != 10) throw new ArgumentOutOfRangeException(nameof(address));

            if (!_entries.TryGetValue(address, out var entry))
            {
                entry = new PNetMeshRoutingEntry
                {
                    Address = (byte[])address.Clone(),
                    EndPoint = endPoint,
                    LastSeen = DateTime.UtcNow
                };
                _entries.Add(address, entry);
            }
            else
            {
                entry.EndPoint = endPoint;
                entry.LastSeen = DateTime.UtcNow;
            }
        }

        public PNetMeshRoutingEntry GetOrCreateEntry(byte[] address)
        {
            if (!_entries.TryGetValue(address, out var entry))
            {
                entry = new PNetMeshRoutingEntry
                {
                    Address = (byte[])address.Clone(),
                    EndPoint = null,
                    LastSeen = DateTime.UtcNow
                };
                _entries.Add(address, entry);
            }
            else
            {
                entry.LastSeen = DateTime.UtcNow;
            }
            return entry;
        }

        public PNetMeshRoutingEntry GetOrCreateEntry(ReadOnlySpan<byte> address)
        {
            var lookup = _entries.GetAlternateLookup<ReadOnlySpan<byte>>();
            if (!lookup.TryGetValue(address, out var entry))
            {
                var key = address.ToArray();
                entry = new PNetMeshRoutingEntry
                {
                    Address = (byte[])key.Clone(),
                    EndPoint = null,
                    LastSeen = DateTime.UtcNow
                };
                _entries.Add(key, entry);
            }
            else
            {
                entry.LastSeen = DateTime.UtcNow;
            }
            return entry;
        }

        internal bool TryAcceptRelayPacket(PNetMeshRelayPacket packet)
        {
            if (packet.Route.Length <= 1)
                return true;

            var remoteAddress = packet.Route[0];

            var entry = GetOrCreateEntry(remoteAddress);
            entry.Tracker ??= new PNetMeshPacketTracker(200);

            return entry.Tracker.TryAdd(packet.SeqNumber);
        }

        internal PNetMeshRelayRouteDecision SelectRelayRoute(
            PNetMeshRelayPacket packet,
            ISet<byte[]> localAddresses,
            IReadOnlyDictionary<byte[], PNetMeshChannel> channels)
        {
            var packetAddress = packet.Address;
            if (packetAddress is not null && localAddresses.Contains(packetAddress))
            {
                var remoteAddress = packet.Route[0];
                var remoteEndPoint = SelectRelayRemoteEndPoint(
                    remoteAddress,
                    packet.CandidateExchange?.Candidates ?? ImmutableArray<PNetMeshCandidate>.Empty,
                    packet.Route.Length);

                return PNetMeshRelayRouteDecision.Local(remoteAddress, remoteEndPoint);
            }

            if (packetAddress is not null
                && channels.TryGetValue(packetAddress, out var directChannel)
                && directChannel.CanRelayDirect)
            {
                return PNetMeshRelayRouteDecision.Direct(directChannel);
            }

            if (packet.HopCount > 0)
            {
                var fanoutChannels = SelectRelayFanoutChannels(channels, packet.Route);
                return fanoutChannels.Count > 0
                    ? PNetMeshRelayRouteDecision.Fanout(fanoutChannels)
                    : PNetMeshRelayRouteDecision.RouteMissed();
            }

            return PNetMeshRelayRouteDecision.HopLimitExceeded();
        }

        internal EndPoint? SelectRelayRemoteEndPoint(
            byte[] remoteAddress,
            ImmutableArray<PNetMeshCandidate> candidates)
        {
            if (TrySelectKnownRoute(remoteAddress, candidates, out var knownRoute))
                return knownRoute;

            return SelectCandidateRemoteEndPoint(candidates);
        }

        internal EndPoint? SelectRelayRemoteEndPoint(
            byte[] remoteAddress,
            ImmutableArray<PNetMeshCandidate> candidates,
            int routeLength)
        {
            if (TrySelectKnownRoute(remoteAddress, candidates, out var knownRoute))
                return knownRoute;

            // Routes include source and target addresses; two or more intermediate relays need a relay-backed response
            // until ICE checks prove a direct candidate pair.
            if (routeLength > 3)
                return null;

            return SelectCandidateRemoteEndPoint(candidates);
        }

        internal static HashSet<byte[]> CreateRelayRouteSet(ImmutableArray<byte[]> route)
        {
            return new HashSet<byte[]>(route, PNetMeshByteArrayComparer.Default);
        }

        internal static bool ShouldRelayToPeer(byte[] peerAddress, HashSet<byte[]> routeSet)
        {
            return !routeSet.Contains(peerAddress);
        }

        internal static IEnumerable<EndPoint> ResolveRemoteEndPoints(IEnumerable<EndPoint?> endpoints)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var endpoint in endpoints)
            {
                var resolved = endpoint switch
                {
                    DnsEndPoint dns => Dns.GetHostAddresses(dns.Host).Select(address => (EndPoint)new IPEndPoint(address, dns.Port)),
                    null => Array.Empty<EndPoint>(),
                    _ => new[] { endpoint }
                };

                foreach (var candidate in resolved.Select(NormalizeRemoteEndPoint))
                {
                    if (candidate is null)
                        continue;

                    if (seen.Add(GetEndPointKey(candidate)))
                        yield return candidate;
                }
            }
        }

        internal static EndPoint? NormalizeRemoteEndPoint(EndPoint? endpoint)
        {
            return PNetMeshEndpointUpdater.NormalizeRemoteEndPoint(endpoint);
        }

        bool TrySelectKnownRoute(
            byte[] remoteAddress,
            ImmutableArray<PNetMeshCandidate> candidates,
            [NotNullWhen(true)] out EndPoint? remoteEndPoint)
        {
            if (TryGetEntry(remoteAddress, out var entry)
                && entry.EndPoint != null
                && candidates.Any(n => entry.EndPoint.Equals(n.Address)))
            {
                remoteEndPoint = entry.EndPoint;
                return true;
            }

            remoteEndPoint = null;
            return false;
        }

        static IReadOnlyList<PNetMeshChannel> SelectRelayFanoutChannels(
            IEnumerable<KeyValuePair<byte[], PNetMeshChannel>> channels,
            ImmutableArray<byte[]> route)
        {
            var routeSet = CreateRelayRouteSet(route);
            var fanoutChannels = new List<PNetMeshChannel>();
            foreach (var (peerAddress, channel) in channels)
            {
                if (channel.HasRoutableSession && ShouldRelayToPeer(peerAddress, routeSet))
                    fanoutChannels.Add(channel);
            }

            return fanoutChannels;
        }

        static EndPoint? SelectCandidateRemoteEndPoint(ImmutableArray<PNetMeshCandidate> candidates)
        {
            // Until ICE checks rank candidate pairs, prefer the observed reflexive address over advertised host endpoints.
            return candidates.FirstOrDefault(n =>
                    n.Type == PNetMeshCandidateType.ServerReflexive
                    && IsUsableRemoteEndPoint(n.Address))?.Address
                ?? candidates.FirstOrDefault(n => IsUsableRemoteEndPoint(n.Address))?.Address
                ?? candidates.FirstOrDefault(n => n.Address != null)?.Address;
        }

        static bool IsUsableRemoteEndPoint(EndPoint? endPoint)
        {
            return endPoint switch
            {
                IPEndPoint ip => !IPAddress.Any.Equals(ip.Address) && !IPAddress.IPv6Any.Equals(ip.Address),
                DnsEndPoint dns => !string.IsNullOrWhiteSpace(dns.Host),
                null => false,
                _ => true
            };
        }

        static string GetEndPointKey(EndPoint? endpoint)
        {
            return endpoint switch
            {
                IPEndPoint ip => $"ip:{ip.AddressFamily}:{ip.Address}:{ip.Port}",
                DnsEndPoint dns => $"dns:{dns.Host}:{dns.Port}",
                null => "null",
                _ => endpoint.ToString() ?? "unknown"
            };
        }
    }

    internal enum PNetMeshRelayRouteKind
    {
        Local,
        Direct,
        Fanout,
        RouteMissed,
        HopLimitExceeded
    }

    internal sealed class PNetMeshRelayRouteDecision
    {
        private PNetMeshRelayRouteDecision(
            PNetMeshRelayRouteKind kind,
            byte[]? localRemoteAddress,
            EndPoint? relayCandidateEndPoint,
            PNetMeshChannel? directChannel,
            IReadOnlyList<PNetMeshChannel> fanoutChannels)
        {
            Kind = kind;
            LocalRemoteAddress = localRemoteAddress;
            RelayCandidateEndPoint = relayCandidateEndPoint;
            DirectChannel = directChannel;
            FanoutChannels = fanoutChannels;
        }

        public PNetMeshRelayRouteKind Kind { get; }

        public byte[]? LocalRemoteAddress { get; }

        public EndPoint? RelayCandidateEndPoint { get; }

        public PNetMeshChannel? DirectChannel { get; }

        public IReadOnlyList<PNetMeshChannel> FanoutChannels { get; }

        public static PNetMeshRelayRouteDecision Local(byte[] remoteAddress, EndPoint? relayCandidateEndPoint)
        {
            return new PNetMeshRelayRouteDecision(
                PNetMeshRelayRouteKind.Local,
                remoteAddress,
                relayCandidateEndPoint,
                directChannel: null,
                Array.Empty<PNetMeshChannel>());
        }

        public static PNetMeshRelayRouteDecision Direct(PNetMeshChannel channel)
        {
            return new PNetMeshRelayRouteDecision(
                PNetMeshRelayRouteKind.Direct,
                localRemoteAddress: null,
                relayCandidateEndPoint: null,
                channel,
                Array.Empty<PNetMeshChannel>());
        }

        public static PNetMeshRelayRouteDecision Fanout(IReadOnlyList<PNetMeshChannel> channels)
        {
            return new PNetMeshRelayRouteDecision(
                PNetMeshRelayRouteKind.Fanout,
                localRemoteAddress: null,
                relayCandidateEndPoint: null,
                directChannel: null,
                channels);
        }

        public static PNetMeshRelayRouteDecision RouteMissed()
        {
            return new PNetMeshRelayRouteDecision(
                PNetMeshRelayRouteKind.RouteMissed,
                localRemoteAddress: null,
                relayCandidateEndPoint: null,
                directChannel: null,
                Array.Empty<PNetMeshChannel>());
        }

        public static PNetMeshRelayRouteDecision HopLimitExceeded()
        {
            return new PNetMeshRelayRouteDecision(
                PNetMeshRelayRouteKind.HopLimitExceeded,
                localRemoteAddress: null,
                relayCandidateEndPoint: null,
                directChannel: null,
                Array.Empty<PNetMeshChannel>());
        }
    }
}
