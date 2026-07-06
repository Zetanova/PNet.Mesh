using System;
using System.Threading;

namespace PNet.Mesh
{
    internal sealed class PNetMeshSessionTable
    {
        const int DefaultCapacity = 16;

        // multi-threading: the server control loop is the single writer while UDP receive callbacks look up sessions.
        // Receiver indexes are monotonic, so published slots are append-only and removals tombstone the session slot.
        uint[] _receiverIndexes;
        PNetMeshSession?[] _sessions;
        int _count;

        public PNetMeshSessionTable(int initialCapacity = DefaultCapacity)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _receiverIndexes = new uint[initialCapacity];
            _sessions = new PNetMeshSession?[initialCapacity];
        }

        public int PublishedCount => Volatile.Read(ref _count);

        public void Add(uint receiverIndex, PNetMeshSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var count = _count;
            var receiverIndexes = _receiverIndexes;
            var sessions = _sessions;

            if (count > 0 && receiverIndex <= receiverIndexes[count - 1])
                throw new InvalidOperationException("Receiver indexes must be added in strictly increasing order.");

            if (count == receiverIndexes.Length)
            {
                var nextCapacity = receiverIndexes.Length * 2;
                var nextReceiverIndexes = new uint[nextCapacity];
                var nextSessions = new PNetMeshSession?[nextCapacity];

                Array.Copy(receiverIndexes, nextReceiverIndexes, receiverIndexes.Length);
                Array.Copy(sessions, nextSessions, sessions.Length);

                Volatile.Write(ref _receiverIndexes, nextReceiverIndexes);
                Volatile.Write(ref _sessions, nextSessions);

                receiverIndexes = nextReceiverIndexes;
                sessions = nextSessions;
            }

            receiverIndexes[count] = receiverIndex;
            Volatile.Write(ref sessions[count], session);
            Volatile.Write(ref _count, count + 1);
        }

        public bool TryGet(uint receiverIndex, out PNetMeshSession session)
        {
            var count = Volatile.Read(ref _count);
            var receiverIndexes = Volatile.Read(ref _receiverIndexes);
            var sessions = Volatile.Read(ref _sessions);
            var index = Array.BinarySearch(receiverIndexes, 0, count, receiverIndex);
            if (index < 0)
            {
                session = default!;
                return false;
            }

            var match = Volatile.Read(ref sessions[index]);
            if (match is null)
            {
                session = default!;
                return false;
            }

            session = match;
            return true;
        }

        public bool Remove(uint receiverIndex)
        {
            var count = Volatile.Read(ref _count);
            var receiverIndexes = Volatile.Read(ref _receiverIndexes);
            var sessions = Volatile.Read(ref _sessions);
            var index = Array.BinarySearch(receiverIndexes, 0, count, receiverIndex);
            if (index < 0 || Volatile.Read(ref sessions[index]) is null)
                return false;

            Volatile.Write(ref sessions[index], null);
            return true;
        }

        public int DisposeClosedSessions()
        {
            var removed = 0;
            var count = _count;
            var sessions = _sessions;

            for (var i = 0; i < count; i++)
            {
                var session = Volatile.Read(ref sessions[i]);
                if (session?.Status != PNetMeshSessionStatus.Closed)
                    continue;

                session.Dispose();
                Volatile.Write(ref sessions[i], null);
                removed++;
            }

            return removed;
        }
    }
}
