using System;
using System.Buffers;
using System.Collections.Generic;

namespace PNet.Mesh
{
    public sealed class PNetMeshPacketBuffer
    {
        sealed class BufferEntry
        {
            public IMemoryOwner<byte>? MemoryOwner { get; set; }

            public Memory<byte> Memory { get; set; }

            public bool IsTracked => MemoryOwner is not null;
        }


        readonly Queue<BufferEntry> _items;
        BufferEntry? _current;
        int _trackedCount;

        public ulong Current => _items.Count == 0 ? Latest - 1 : Latest + (ulong)_items.Count - 1;

        public ulong Latest { get; private set; }

        public int Count => _trackedCount;

        public PNetMeshPacketBuffer()
        {
            _items = new Queue<BufferEntry>();
        }

        public Memory<byte> Rent(int size)
        {
            var buffer = MemoryPool<byte>.Shared.Rent(size + 48);
            _current = new BufferEntry
            {
                MemoryOwner = buffer,
                Memory = buffer.Memory
            };
            _items.Enqueue(_current);
            _trackedCount++;
            return buffer.Memory;
        }

        public Memory<byte> SliceCurrent(int size)
        {
            if (_current is null)
                throw new InvalidOperationException("No current packet buffer is available.");

            _current.Memory = _current.Memory.Slice(0, size);
            return _current.Memory;
        }

        public void RemoveUntil(ulong counter)
        {
            if (counter < Latest)
                return;
            if (_items.Count == 0)
            {
                Latest = counter + 1;
                return;
            }
            if (Current < counter)
            {
                while (_items.Count > 0)
                {
                    var item = _items.Dequeue();
                    if (item.MemoryOwner is { } owner)
                    {
                        owner.Dispose();
                        _trackedCount--;
                    }
                }
                Latest = counter + 1;
                return;
            }

            if (Latest <= counter)
            {
                BufferEntry entry;
                for (var i = Latest; i <= counter; i++)
                {
                    entry = _items.Dequeue();
                    if (entry.MemoryOwner is { } owner)
                    {
                        owner.Dispose();
                        _trackedCount--;
                    }
                }
                Latest = counter + 1;
            }
        }

        public void DiscardCurrent()
        {
            if (_current is null || _items.Count == 0)
                return;

            var items = _items.ToArray();
            if (!ReferenceEquals(items[^1], _current))
                throw new InvalidOperationException("Current packet is not the newest buffered packet.");

            _items.Clear();
            for (var i = 0; i < items.Length - 1; i++)
                _items.Enqueue(items[i]);

            if (_current.MemoryOwner is { } owner)
            {
                owner.Dispose();
                _trackedCount--;
            }

            _current = null;
        }

        public void AddUntracked(ulong counter)
        {
            if (_trackedCount == 0)
            {
                while (_items.Count > 0)
                    _items.Dequeue();
                Latest = counter + 1;
                return;
            }

            var next = Latest + (ulong)_items.Count;
            if (counter < next)
                return;

            while (next <= counter)
            {
                _items.Enqueue(new BufferEntry
                {
                    Memory = Memory<byte>.Empty
                });
                next++;
            }
        }

        public int CountOutstanding(ulong ackSeqNumber)
        {
            var count = 0;
            var counter = Latest;
            foreach (var entry in _items)
            {
                if (entry.IsTracked && counter >= ackSeqNumber)
                    count++;
                counter++;
            }
            return count;
        }

        static bool IsSet(ReadOnlyMemory<byte> bitmap, int index)
        {
            var span = bitmap.Span;
            return (span[index >> 3] & (1 << (index & 7))) != 0;
        }

        public IEnumerable<Memory<byte>> GetSequence(ReadOnlyMemory<byte> bitmap)
        {
            var bitCount = bitmap.Length * 8;
            var index = 0;
            foreach (var entry in _items)
            {
                if (index >= bitCount)
                    yield break;

                if (entry.IsTracked && IsSet(bitmap, index))
                    yield return entry.Memory;

                index++;
            }
        }

        public IEnumerable<Memory<byte>> GetSequence(byte[] bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            return GetSequence((ReadOnlyMemory<byte>)bitmap);
        }

        public IEnumerable<Memory<byte>> GetMissingSequence(ReadOnlyMemory<byte> receivedBitmap)
        {
            var bitCount = receivedBitmap.Length * 8;
            var index = 0;
            foreach (var entry in _items)
            {
                if (index >= bitCount)
                    yield break;

                if (entry.IsTracked && !IsSet(receivedBitmap, index))
                    yield return entry.Memory;

                index++;
            }
        }

        public IEnumerable<Memory<byte>> GetMissingSequence(byte[] receivedBitmap)
        {
            if (receivedBitmap == null)
                throw new ArgumentNullException(nameof(receivedBitmap));

            return GetMissingSequence((ReadOnlyMemory<byte>)receivedBitmap);
        }

        public void RemoveSequence(ReadOnlyMemory<byte> receivedBitmap)
        {
            var bitCount = receivedBitmap.Length * 8;
            var index = 0;
            foreach (var entry in _items)
            {
                if (index >= bitCount)
                    break;

                if (!IsSet(receivedBitmap, index++) || entry.MemoryOwner is not { } owner)
                    continue;

                owner.Dispose();
                entry.MemoryOwner = null;
                entry.Memory = Memory<byte>.Empty;
                _trackedCount--;
            }
        }

        public void RemoveSequence(byte[] receivedBitmap)
        {
            if (receivedBitmap == null)
                throw new ArgumentNullException(nameof(receivedBitmap));

            RemoveSequence((ReadOnlyMemory<byte>)receivedBitmap);
        }

        public IEnumerable<Memory<byte>> GetSequence()
        {
            foreach (var entry in _items)
            {
                if (entry.IsTracked)
                    yield return entry.Memory;
            }
        }
    }
}
