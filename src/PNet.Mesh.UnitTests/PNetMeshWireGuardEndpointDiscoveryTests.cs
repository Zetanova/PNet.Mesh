using PNet.Mesh;
using System;
using System.Net;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshWireGuardEndpointDiscoveryTests
    {
        [Fact]
        public void hint_is_untrusted_and_probe_keeps_relay_current()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var relay = Endpoint(10000);
            var candidate = Endpoint(20000);
            var discovery = new PNetMeshWireGuardEndpointDiscovery(
                relay,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));

            Assert.True(discovery.TryApplyEndpointHint(candidate, now));

            Assert.Equal(relay, discovery.CurrentEndpoint);
            Assert.Equal(candidate, discovery.CandidateEndpoint);
            Assert.Null(discovery.DirectEndpoint);

            Assert.True(discovery.TryBeginDirectProbe(now.AddSeconds(1), out var probeEndpoint));
            Assert.Equal(candidate, probeEndpoint);
            Assert.False(discovery.TryBeginDirectProbe(now.AddSeconds(2), out _));
            Assert.Equal(relay, discovery.CurrentEndpoint);
        }

        [Fact]
        public void same_hint_after_probe_ttl_does_not_start_second_probe()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var relay = Endpoint(10000);
            var candidate = Endpoint(20000);
            var discovery = new PNetMeshWireGuardEndpointDiscovery(
                relay,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5));

            Assert.True(discovery.TryApplyEndpointHint(candidate, now));
            Assert.True(discovery.TryBeginDirectProbe(now.AddSeconds(1), out _));

            Assert.False(discovery.TryApplyEndpointHint(candidate, now.AddSeconds(7)));
            Assert.False(discovery.TryBeginDirectProbe(now.AddSeconds(8), out _));
            Assert.Equal(candidate, discovery.CandidateEndpoint);
            Assert.Equal(relay, discovery.CurrentEndpoint);
        }

        [Fact]
        public void authenticated_direct_response_promotes_current_endpoint()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var relay = Endpoint(10000);
            var candidate = Endpoint(20000);
            var discovery = new PNetMeshWireGuardEndpointDiscovery(
                relay,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));

            Assert.True(discovery.TryApplyEndpointHint(candidate, now));
            Assert.True(discovery.TryBeginDirectProbe(now.AddSeconds(1), out _));
            Assert.True(discovery.TryPromoteAuthenticatedDirectEndpoint(candidate, now.AddSeconds(2)));

            Assert.Equal(candidate, discovery.CurrentEndpoint);
            Assert.Equal(candidate, discovery.DirectEndpoint);
            Assert.Null(discovery.CandidateEndpoint);
        }

        [Fact]
        public void unauthenticated_or_unmatched_response_does_not_promote_and_can_fallback()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var relay = Endpoint(10000);
            var candidate = Endpoint(20000);
            var attacker = Endpoint(30000);
            var discovery = new PNetMeshWireGuardEndpointDiscovery(
                relay,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));

            Assert.True(discovery.TryApplyEndpointHint(candidate, now));
            Assert.True(discovery.TryBeginDirectProbe(now.AddSeconds(1), out _));

            Assert.False(discovery.TryPromoteAuthenticatedDirectEndpoint(attacker, now.AddSeconds(2)));
            discovery.FailDirectProbe();

            Assert.Equal(relay, discovery.CurrentEndpoint);
            Assert.Null(discovery.CandidateEndpoint);
            Assert.Null(discovery.DirectEndpoint);
        }

        [Fact]
        public void stale_hint_and_probe_do_not_promote()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var relay = Endpoint(10000);
            var candidate = Endpoint(20000);
            var discovery = new PNetMeshWireGuardEndpointDiscovery(
                relay,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));

            Assert.True(discovery.TryApplyEndpointHint(candidate, now));
            Assert.False(discovery.TryBeginDirectProbe(now.AddSeconds(11), out _));
            Assert.Null(discovery.CandidateEndpoint);

            Assert.True(discovery.TryApplyEndpointHint(candidate, now.AddSeconds(20)));
            Assert.True(discovery.TryBeginDirectProbe(now.AddSeconds(21), out _));
            Assert.False(discovery.TryPromoteAuthenticatedDirectEndpoint(candidate, now.AddSeconds(27)));

            Assert.Equal(relay, discovery.CurrentEndpoint);
            Assert.Null(discovery.CandidateEndpoint);
            Assert.Null(discovery.DirectEndpoint);
        }

        [Fact]
        public void same_hint_refresh_preserves_active_probe()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var relay = Endpoint(10000);
            var candidate = Endpoint(20000);
            var discovery = new PNetMeshWireGuardEndpointDiscovery(
                relay,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));

            Assert.True(discovery.TryApplyEndpointHint(candidate, now));
            Assert.True(discovery.TryBeginDirectProbe(now.AddSeconds(1), out _));
            Assert.False(discovery.TryApplyEndpointHint(candidate, now.AddSeconds(2)));
            Assert.True(discovery.TryPromoteAuthenticatedDirectEndpoint(candidate, now.AddSeconds(3)));

            Assert.Equal(candidate, discovery.CurrentEndpoint);
            Assert.Equal(candidate, discovery.DirectEndpoint);
            Assert.Null(discovery.CandidateEndpoint);
        }

        [Fact]
        public void explicit_fallback_returns_to_relay_after_direct_promotion()
        {
            var now = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
            var relay = Endpoint(10000);
            var candidate = Endpoint(20000);
            var discovery = new PNetMeshWireGuardEndpointDiscovery(
                relay,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));

            Assert.True(discovery.TryApplyEndpointHint(candidate, now));
            Assert.True(discovery.TryBeginDirectProbe(now.AddSeconds(1), out _));
            Assert.True(discovery.TryPromoteAuthenticatedDirectEndpoint(candidate, now.AddSeconds(2)));

            discovery.FallbackToRelay();

            Assert.Equal(relay, discovery.CurrentEndpoint);
            Assert.Null(discovery.CandidateEndpoint);
            Assert.Null(discovery.DirectEndpoint);
        }

        static IPEndPoint Endpoint(int port)
        {
            return new IPEndPoint(IPAddress.Parse("203.0.113.10"), port);
        }
    }
}
