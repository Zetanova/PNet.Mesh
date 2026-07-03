using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace PNet.Mesh
{
    public sealed class PNetMeshRoutingEntry
    {
        /// <summary>
        /// Peer Address are the first 10 bytes from the SHA-1 of the PublicKey
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
        //todo extract routing code from server 

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
    }
}
