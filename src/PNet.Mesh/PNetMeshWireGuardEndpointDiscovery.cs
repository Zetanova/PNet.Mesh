using System;
using System.Net;

namespace PNet.Mesh
{
    public sealed class PNetMeshWireGuardEndpointDiscovery
    {
        readonly EndPoint _relayEndpoint;
        readonly TimeSpan _hintTtl;
        readonly TimeSpan _probeTtl;

        EndPoint _candidateEndpoint;
        DateTimeOffset _candidateExpiresAt;
        DateTimeOffset _probeExpiresAt;
        bool _probeActive;
        bool _probeAttempted;

        public PNetMeshWireGuardEndpointDiscovery(
            EndPoint relayEndpoint,
            TimeSpan hintTtl,
            TimeSpan probeTtl)
        {
            if (hintTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(hintTtl));
            if (probeTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(probeTtl));

            _relayEndpoint = relayEndpoint;
            _hintTtl = hintTtl;
            _probeTtl = probeTtl;
        }

        public EndPoint CurrentEndpoint => DirectEndpoint ?? _relayEndpoint;

        public EndPoint DirectEndpoint { get; private set; }

        public EndPoint CandidateEndpoint => _candidateEndpoint;

        public bool TryApplyEndpointHint(EndPoint candidateEndpoint, DateTimeOffset now)
        {
            if (candidateEndpoint is null)
                return false;

            if (DirectEndpoint is not null && EndPointsEqual(DirectEndpoint, candidateEndpoint))
                return false;

            if (_candidateEndpoint is not null && _candidateExpiresAt <= now)
                ClearCandidate();

            if (_candidateEndpoint is not null && EndPointsEqual(_candidateEndpoint, candidateEndpoint))
            {
                _candidateExpiresAt = now + _hintTtl;
                if (_probeActive && _probeExpiresAt <= now)
                {
                    _probeExpiresAt = default;
                    _probeActive = false;
                }

                return false;
            }

            _candidateEndpoint = candidateEndpoint;
            _candidateExpiresAt = now + _hintTtl;
            _probeExpiresAt = default;
            _probeActive = false;
            _probeAttempted = false;
            return true;
        }

        public bool TryBeginDirectProbe(DateTimeOffset now, out EndPoint candidateEndpoint)
        {
            candidateEndpoint = null;
            if (_probeAttempted)
            {
                if (_probeActive && _probeExpiresAt <= now)
                    _probeActive = false;

                return false;
            }

            if (_probeActive)
            {
                if (_probeExpiresAt <= now)
                    ClearCandidate();

                return false;
            }

            if (!HasFreshCandidate(now))
                return false;

            _probeActive = true;
            _probeAttempted = true;
            _probeExpiresAt = now + _probeTtl;
            candidateEndpoint = _candidateEndpoint;
            return true;
        }

        public bool TryPromoteAuthenticatedDirectEndpoint(EndPoint endpoint, DateTimeOffset now)
        {
            if (endpoint is null || !HasFreshCandidate(now))
                return false;

            if (!_probeActive || _probeExpiresAt <= now)
            {
                ClearCandidate();
                return false;
            }

            if (!EndPointsEqual(_candidateEndpoint, endpoint))
                return false;

            DirectEndpoint = _candidateEndpoint;
            ClearCandidate();
            return true;
        }

        public void FailDirectProbe()
        {
            ClearCandidate();
        }

        public void FallbackToRelay()
        {
            DirectEndpoint = null;
            ClearCandidate();
        }

        bool HasFreshCandidate(DateTimeOffset now)
        {
            if (_candidateEndpoint is not null && _candidateExpiresAt > now)
                return true;

            ClearCandidate();
            return false;
        }

        void ClearCandidate()
        {
            _candidateEndpoint = null;
            _candidateExpiresAt = default;
            _probeExpiresAt = default;
            _probeActive = false;
            _probeAttempted = false;
        }

        static bool EndPointsEqual(EndPoint left, EndPoint right)
        {
            return left switch
            {
                IPEndPoint leftIp when right is IPEndPoint rightIp
                    => leftIp.Port == rightIp.Port && leftIp.Address.Equals(rightIp.Address),
                DnsEndPoint leftDns when right is DnsEndPoint rightDns
                    => leftDns.Port == rightDns.Port
                       && string.Equals(leftDns.Host, rightDns.Host, StringComparison.OrdinalIgnoreCase),
                _ => left.Equals(right)
            };
        }
    }
}
