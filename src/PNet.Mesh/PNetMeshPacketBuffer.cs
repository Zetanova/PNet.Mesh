using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PNet.Actor.Mesh
{
    public sealed class PNetMeshPacketBuffer
    {
        sealed class BufferEntry
        {
            public IMemoryOwner<byte> MemoryOwner { get; init; }

            public Memory<byte> Memory { get; set; }
        }


        Queue<BufferEntry> _items;
        BufferEntry _current;

        public ulong Current => Latest + (ulong)_items.Count - 1;

        public ulong Latest { get; private set; }

        public int Count => _items.Count;

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
            return buffer.Memory;
        }

        public Memory<byte> SliceCurrent(int size)
        {
            _current.Memory = _current.Memory.Slice(0, size);
            return _current.Memory;
        }

        public void RemoveUntil(ulong counter)
        {
            if (Current < counter)
                throw new ArgumentOutOfRangeException(nameof(counter));

            if (Latest <= counter)
            {
                BufferEntry entry;
                for (var i = Latest; i <= counter; i++)
                {
                    entry = _items.Dequeue();
                    entry.MemoryOwner.Dispose();
                }
                Latest = counter + 1;
            }
        }

        public IEnumerable<Memory<byte>> GetSequence(byte[] bitmap)
        {
            //todo refactor to loop and take readonlyspan as arg
            var bits = new BitArray(bitmap);
            return _items
                .Take(bits.Length).Where((n, i) => bits[i])
                //.Where((n, i) => i >= bits.Length || bits[i])
                .Select(n => n.Memory);
        }
    }
}
