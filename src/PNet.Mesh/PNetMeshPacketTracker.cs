using System;
using System.Buffers;

namespace PNet.Actor.Mesh
{
    /// <summary>
    /// Implementation of Anti-Replay Algorithm without Bit Shifting.
    /// <see href="https://tools.ietf.org/html/rfc6479">RFC 6479</see>).
    /// </summary>
    public sealed class PNetMeshPacketTracker : IDisposable
    {
        private const int RedundantBitShift = 6; //bitcount of CounterRedundantBits-1
        private const int CounterRedundantBits = sizeof(ulong) * 8;

        int _wordCount;
        ulong[] _bitmap;

        public int Size => (_wordCount * sizeof(ulong)) - CounterRedundantBits;

        public ulong Current { get; private set; }

        public ulong Latest => Current > (ulong)Size ? Current - (ulong)Size : 0;

        public PNetMeshPacketTracker(int counterSize = 2000)
        {
            //todo smaller implentation for reduced counter

            _wordCount = (int)Math.Ceiling((double)(counterSize + CounterRedundantBits) / sizeof(ulong));

            _bitmap = ArrayPool<ulong>.Shared.Rent(_wordCount);
            _bitmap[0] = 0;
        }

        public bool TryAdd(ulong counter)
        {
            var index = counter >> RedundantBitShift;
            if (counter > Current)
            {
                var currentIndex = Current >> RedundantBitShift;
                var diff = (uint)Math.Min(index - currentIndex, (ulong)_wordCount);
                for (uint i = 1; i <= diff; i++)
                    _bitmap[(currentIndex + i) % (ulong)_wordCount] = 0;
                Current = counter;
            }
            else if (Current - counter > (ulong)Size)
            {
                //packet expired
                return false;
            }

            index %= (ulong)_wordCount;
            var oldValue = _bitmap[index];
            var newValue = oldValue | (1ul << (int)(counter & (CounterRedundantBits - 1)));
            _bitmap[index] = newValue;

            return oldValue != newValue;
        }

        /// <summary>
        /// bit sequence of segments from counter onwards
        /// </summary>
        /// <param name="counter"></param>
        /// <param name="bitmap"></param>
        /// <param name="bytesUsed"></param>
        public void GetBitmap(ulong counter, Span<byte> bitmap, out int bytesWritten)
        {
            if (counter < Latest)
                throw new ArgumentOutOfRangeException(nameof(counter));
            if (Current < counter)
            {
                bytesWritten = 0;
                return;
            }

            var bitCount = (int)(Current - counter + 1);
            if (bitmap.Length * 8 < bitCount)
                throw new ArgumentOutOfRangeException(nameof(bitmap));

            //todo over bit shifting
            //var index = (int)((counter >> RedundantBitShift) % WordsCount);
            //var value = _bitmap[index];
            //var bitIndex = (int)(counter & (CounterRedundantBits - 1));


            int bitmapIndex = 0, bitOfBitmap = 0;
            int index; ulong value; int bitIndex;

            bitmap[0] = 0;
            for (uint i = 0; i < bitCount; i++)
            {
                index = (int)(((counter + i) >> RedundantBitShift) % (ulong)_wordCount);
                bitIndex = (int)((counter + i) & (CounterRedundantBits - 1));
                value = _bitmap[index];
                if (value == (value | (1ul << bitIndex)))
                    bitmap[bitmapIndex] |= (byte)(1 << bitOfBitmap);

                bitOfBitmap++;
                if (bitOfBitmap == 8)
                {
                    bitOfBitmap = 0;
                    bitmap[++bitmapIndex] = 0;
                }
            }
            bytesWritten = bitmapIndex + 1;
        }

        public void Dispose()
        {
            if (_bitmap.Length > 0)
            {
                ArrayPool<ulong>.Shared.Return(_bitmap, false);
                _bitmap = Array.Empty<ulong>();
            }
        }


        /// <summary>
        /// shift bit(1) sequence to right
        /// </summary>
        /// <param name="bitmap">bit field to align right</param>
        /// <param name="bytesUsed">byte count used</param>
        /// <returns>bit count shifted</returns>
        public static uint RightShift(Span<byte> bitmap, out int bytesUsed)
        {
            byte value; int bitIndex = 0; int index; int i;

            for (index = 0; index < bitmap.Length; index++)
            {
                value = bitmap[index];
                for (bitIndex = 0; bitIndex < 8; bitIndex++)
                {
                    if (value != (value | (1 << bitIndex)))
                        break;
                }
                if (bitIndex < 8)
                    break;
            }

            if (index > 0)
            {
                bitmap[index..].CopyTo(bitmap);
                bitmap[^index..].Clear();
            }

            if (bitIndex > 0)
            {
                bitmap = bitmap.Slice(0, bitmap.Length - index);
                for (i = 0; i < bitmap.Length - 1; i++)
                    bitmap[i] = (byte)(bitmap[i + 1] << (8 - bitIndex) | bitmap[i] >> bitIndex);
                bitmap[i] = (byte)(bitmap[i] >> bitIndex);
            }

            for (i = bitmap.Length - 1; i >= 0; i--)
                if (bitmap[i] != 0x00) break;
            bytesUsed = i + 1;

            return (uint)(index * 8 + bitIndex);
        }
    }
}
