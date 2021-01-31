using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;

namespace PNet.Actor.Mesh
{
    public sealed class PNetMeshAgent
    {
        /// <summary>
        /// ICE: Lite Implementation
        /// Lite implementations only utilize host candidates. 
        /// </summary>
        public readonly bool Lite;
    }


    public sealed class PNetMeshTopology
    {
    }

    public sealed class PNetMeshPeer : IEquatable<PNetMeshPeer>
    {
        public byte[] PublicKey { get; init; }

        public string[] EndPoints { get; init; }

        public override bool Equals(object obj)
        {
            return Equals(obj as PNetMeshPeer);
        }

        public bool Equals(PNetMeshPeer other)
        {
            return other != null
                && PNetMeshByteArrayComparer.Default.Equals(PublicKey, other.PublicKey);
        }

        public override int GetHashCode()
        {
            return PNetMeshByteArrayComparer.Default.GetHashCode(PublicKey);
        }

        public static bool operator ==(PNetMeshPeer left, PNetMeshPeer right)
        {
            return EqualityComparer<PNetMeshPeer>.Default.Equals(left, right);
        }

        public static bool operator !=(PNetMeshPeer left, PNetMeshPeer right)
        {
            return !(left == right);
        }
    }

    public sealed class PNetMeshPair
    {

    }

    public sealed class PNetMeshCandidateExchange
    {
        public bool Lite { get; init; }

        public uint CheckPacing { get; init; }

        public string UserPass { get; init; }

        public ImmutableArray<PNetMeshCandidate> Candidates { get; init; }
    }

    public sealed class PNetMeshCandidate
    {
        /// <summary>
        /// transport address
        /// </summary>
        public EndPoint Address { get; init; }

        public PNetMeshProtocolType Protocol { get; init; }

        public PNetMeshCandidateType Type { get; init; }

        /// <summary>
        /// The base of the server-reflexive candidate is the host candidate 
        /// from which the Allocate or Binding request was sent.
        /// The base of a relayed candidate is that candidate itself.
        /// If a relayed candidate is identical to a host candidate(which can happen in rare cases), 
        /// the relayed candidate MUST be discarded.
        /// </summary>
        public EndPoint Base { get; init; }

        /// <summary>
        ///   o  They have the same type (host, relayed, server reflexive, or peer  reflexive).
        ///   o Their bases have the same IP address(the ports can be different).
        ///   o For reflexive and relayed candidates, the STUN or TURN servers used to obtain them 
        ///   have the same IP address(the IP address used by the agent to contact the STUN or TURN server).
        ///   o They were obtained using the same transport protocol(TCP, UDP).
        /// </summary>
        public string Foundation { get; init; }

        public byte ComponentId { get; init; } = 1; //ICE: not used

        /// <summary>
        /// Recommended Formula
        /// priority = (2^24)*(type preference) + (2^8)*(local preference) + (2^0)*(256 - component ID)
        /// 
        ///  The type preference MUST be an integer from 0 (lowest preference) to 126 (highest preference)
        ///  The local preference MUST be an integer from 0 (lowest preference) to 65535 (highest preference)
        ///  When there is only a single IP address, this value SHOULD be set to 65535.
        ///  If an ICE agent is dual stack, the local preference SHOULD be set according to the current best practice described in [RFC8421]
        ///  
        /// The RECOMMENDED values for type preferences are 126 for host candidates, 
        /// 110 for peer-reflexive candidates, 100 for server-reflexive candidates, and 0 for relayed candidates.
        /// </summary>
        public uint Priority { get; init; } = 0;
    }

    public enum PNetMeshCandidateType
    {
        Host = 0, //Host candidates are obtained by binding to ports on an IP address attached to an interface (physical or virtual, including VPN interfaces) on the host.
        ServerReflexive = 1, //ICE: gathered using STUN or TURN
        PeerReflexive = 2, //ICE: consequence of connectivity checks
        Relayed = 3 //ICE: obtained through TURN
    }

    public enum PNetMeshProtocolType
    {
        UDP = 0,
        TCP = 1
    }

    public sealed class PNetMeshCandidatePair
    {
        /*
         The ICE agent computes a priority for each candidate pair.  Let G be
        the priority for the candidate provided by the controlling agent.
        Let D be the priority for the candidate provided by the controlled
        agent.  The priority for a pair is computed as follows:

        pair priority = 2^32*MIN(G,D) + 2*MAX(G,D) + (G>D?1:0)
        */

        public readonly PNetMeshCandidate Local;

        public readonly PNetMeshCandidate Remote;

        public readonly bool Default;

        public readonly bool Valid;

        public readonly bool Nominated;

        public readonly PNetMeshCandidatePairState State;
    }

    public enum PNetMeshCandidatePairState
    {
        Frozen = 0,
        Waiting,
        InProgress,
        Succeeded,
        Failed
    }
}
