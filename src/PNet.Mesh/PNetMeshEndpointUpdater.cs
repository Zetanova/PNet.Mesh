using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace PNet.Mesh
{
    internal sealed class PNetMeshEndpointUpdater
    {
        readonly PNetMeshRouter _router;
        readonly ILogger _logger;

        public PNetMeshEndpointUpdater(PNetMeshRouter router, ILogger logger)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ApplyAuthenticatedEndpointUpdate(PNetMeshSession session, PNetMeshControlCommands.Receive cmd)
        {
            if (cmd.LocalEndPoint is not null)
                session.UpdateLocalEndpoint(cmd.LocalEndPoint);

            if (cmd.RelayCandidateEndPoint is not null)
            {
                if (session.TryApplyDirectEndpointHint(cmd.RelayCandidateEndPoint, DateTimeOffset.UtcNow))
                {
                    _logger.LogInformation(
                        "event=wireguard_endpoint_hint_queued session={sessionIndex} endpoint_id={endpointId}",
                        session.SenderIndex,
                        PNetMeshDiagnosticRedactor.EndpointId(cmd.RelayCandidateEndPoint));
                }
                else if (!session.SupportsDirectEndpointDiscovery)
                {
                    session.ConfirmRemoteEndpoint(cmd.RelayCandidateEndPoint);
                    _router.SetEntry(session.RemoteAddress, session.RemoteEndPoint);
                    _logger.LogInformation(
                        "event=relay_endpoint_confirmed session={sessionIndex} mode=legacy_hint endpoint_id={endpointId}",
                        session.SenderIndex,
                        PNetMeshDiagnosticRedactor.EndpointId(cmd.RelayCandidateEndPoint));
                }
                else
                {
                    _logger.LogInformation(
                        "event=wireguard_endpoint_hint_ignored session={sessionIndex} endpoint_id={endpointId}",
                        session.SenderIndex,
                        PNetMeshDiagnosticRedactor.EndpointId(cmd.RelayCandidateEndPoint));
                }

                return;
            }

            if (cmd.RemoteEndPoint is null)
                return;

            var remoteEndPoint = NormalizeRemoteEndPoint(cmd.RemoteEndPoint);
            if (remoteEndPoint is null)
                return;

            if (EndPointsEqual(session.RemoteEndPoint, remoteEndPoint))
            {
                _router.SetEntry(session.RemoteAddress, session.RemoteEndPoint);
                return;
            }

            if (session.TryPromoteDirectEndpoint(remoteEndPoint, DateTimeOffset.UtcNow))
            {
                _logger.LogInformation(
                    "event=wireguard_direct_promoted session={sessionIndex} endpoint_id={endpointId}",
                    session.SenderIndex,
                    PNetMeshDiagnosticRedactor.EndpointId(remoteEndPoint));
            }
            else
            {
                _logger.LogInformation(
                    "event=wireguard_direct_fallback session={sessionIndex} endpoint_id={endpointId}",
                    session.SenderIndex,
                    PNetMeshDiagnosticRedactor.EndpointId(remoteEndPoint));
                session.ConfirmRemoteEndpoint(remoteEndPoint);
            }

            _router.SetEntry(session.RemoteAddress, session.RemoteEndPoint);
        }

        public void ApplyLegacyEndpointUpdate(PNetMeshSession session, PNetMeshControlCommands.Receive cmd)
        {
            if (cmd.LocalEndPoint is not null)
                session.UpdateLocalEndpoint(cmd.LocalEndPoint);

            var remoteEndPoint = cmd.RelayCandidateEndPoint ?? cmd.RemoteEndPoint;
            if (remoteEndPoint is null)
                return;

            session.ConfirmRemoteEndpoint(remoteEndPoint);
            _router.SetEntry(session.RemoteAddress, session.RemoteEndPoint);
            _logger.LogInformation(
                "event=relay_endpoint_confirmed session={sessionIndex} mode=legacy endpoint_id={endpointId}",
                session.SenderIndex,
                PNetMeshDiagnosticRedactor.EndpointId(remoteEndPoint));
        }

        public static EndPoint? NormalizeRemoteEndPoint(EndPoint? endpoint)
        {
            return endpoint switch
            {
                IPEndPoint ip when ip.Address.IsIPv4MappedToIPv6 => new IPEndPoint(ip.Address.MapToIPv4(), ip.Port),
                _ => endpoint
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

        static bool EndPointsEqual(EndPoint? left, EndPoint? right)
        {
            return string.Equals(GetEndPointKey(left), GetEndPointKey(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
