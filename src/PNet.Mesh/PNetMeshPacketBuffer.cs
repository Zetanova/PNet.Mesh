using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PNet.Mesh
{
    public sealed class PNetMeshPacketBuffer
    {
        sealed class BufferEntry
        {
            public IMemoryOwner<byte> MemoryOwner { get; set; }

            public Memory<byte> Memory { get; set; }

            public bool IsTracked => MemoryOwner is not null;
        }


        Queue<BufferEntry> _items;
        BufferEntry _current;
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
                    if (item.IsTracked)
                    {
                        item.MemoryOwner.Dispose();
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
                    if (entry.IsTracked)
                    {
                        entry.MemoryOwner.Dispose();
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

            if (_current.IsTracked)
            {
                _current.MemoryOwner.Dispose();
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

        public IEnumerable<Memory<byte>> GetSequence(byte[] bitmap)
        {
            //todo refactor to loop and take readonlyspan as arg
            var bits = new BitArray(bitmap);
            return _items
                .Take(bits.Length).Where((n, i) => n.IsTracked && bits[i])
                //.Where((n, i) => i >= bits.Length || bits[i])
                .Select(n => n.Memory);
        }

        public IEnumerable<Memory<byte>> GetMissingSequence(byte[] receivedBitmap)
        {
            var bits = new BitArray(receivedBitmap);
            return _items
                .Take(bits.Length)
                .Where((n, i) => n.IsTracked && !bits[i])
                .Select(n => n.Memory);
        }

        public void RemoveSequence(byte[] receivedBitmap)
        {
            var bits = new BitArray(receivedBitmap);
            var index = 0;
            foreach (var entry in _items)
            {
                if (index >= bits.Length)
                    break;

                if (!bits[index++] || !entry.IsTracked)
                    continue;

                entry.MemoryOwner.Dispose();
                entry.MemoryOwner = null;
                entry.Memory = Memory<byte>.Empty;
                _trackedCount--;
            }
        }

        public IEnumerable<Memory<byte>> GetSequence()
        {
            return _items.Where(n => n.IsTracked).Select(n => n.Memory);
        }
    }
}
