using PNet.Mesh;
using System;
using System.Collections;
using System.Reflection;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshPacketTrackerTest
    {
        [Fact]
        public void detect_no_replay()
        {
            using var tracker = new PNetMeshPacketTracker();

            var counters = new ulong[] { 1, 29, 49, 48, 50 };

            for (int i = 0; i < counters.Length; i++)
                Assert.True(tracker.TryAdd(counters[i]));
        }

        [Fact]
        public void detect_replay()
        {
            using var tracker = new PNetMeshPacketTracker();

            var counters = new ulong[] { 1, 29, 49, 48, 50 };

            for (int i = 0; i < counters.Length; i++)
                Assert.True(tracker.TryAdd(counters[i]));

            for (int i = 1; i < counters.Length - 1; i++)
                Assert.False(tracker.TryAdd(counters[i]));
        }

        [Fact]
        public void detect_expired()
        {
            using var tracker = new PNetMeshPacketTracker();

            var counters = new ulong[] { 1, 29, 49, 48, 50, 2000, 2050 };

            for (int i = 0; i < counters.Length; i++)
                Assert.True(tracker.TryAdd(counters[i]));

            Assert.False(tracker.TryAdd(2));
        }

        [Theory]
        [InlineData(4098)] //not  4096
        [InlineData(2048)]
        [InlineData(2000)]
        [InlineData(1024)]
        [InlineData(1000)]
        [InlineData(64)]
        [InlineData(50)]
        [InlineData(32)]
        public void detect_window(int counterSize)
        {
            using var tracker = new PNetMeshPacketTracker(counterSize);
            Assert.True(counterSize <= tracker.Size);

            ulong i;
            for (i = 0; i < 5000; i += 3)
                Assert.True(tracker.TryAdd(i));

            Assert.Equal(i - 3, tracker.Current);

            Assert.False(tracker.TryAdd(0));
            Assert.False(tracker.TryAdd(3));

            var l = i - (ulong)tracker.Size - 3;
            Assert.Equal(l, tracker.Latest);
            Assert.Equal(l % 3 != 0, tracker.TryAdd(l));
            Assert.False(tracker.TryAdd(l));

            for (i = 0; i < 5000; i++)
            {
                if (tracker.TryAdd(i))
                    break;
            }

            Assert.True(l < i);
        }

        [Fact]
        public void get_bitmap_rejects_counter_before_latest_regression()
        {
            using var tracker = new PNetMeshPacketTracker(32);

            for (ulong i = 0; i <= 64; i++)
                Assert.True(tracker.TryAdd(i));

            Assert.True(tracker.Latest > 0);

            var buffer = new byte[16];
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.GetBitmap(tracker.Latest - 1, buffer, out _));
        }

        [Fact]
        public void get_bitmap_rejects_undersized_buffer_regression()
        {
            using var tracker = new PNetMeshPacketTracker();

            for (ulong i = 0; i <= 8; i++)
                Assert.True(tracker.TryAdd(i));

            var buffer = new byte[1];
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.GetBitmap(0, buffer, out _));
        }

        [Theory]
        [InlineData(32, 32, 2)]
        [InlineData(33, 40, 2)]
        [InlineData(64, 64, 2)]
        [InlineData(65, 72, 3)]
        [InlineData(128, 128, 3)]
        [InlineData(2000, 2000, 33)]
        [InlineData(4098, 4104, 66)]
        public void tracker_rents_only_required_words_for_effective_window_regression(
            int counterSize,
            int expectedSize,
            int expectedWordCount)
        {
            using var tracker = new PNetMeshPacketTracker(counterSize);

            var field = typeof(PNetMeshPacketTracker).GetField("_wordCount", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("_wordCount field was not found.");
            var wordCount = Assert.IsType<int>(field.GetValue(tracker));

            Assert.Equal(expectedSize, tracker.Size);
            Assert.Equal(expectedWordCount, wordCount);
        }

        [Fact]
        public void get_bitmap_fills_exact_byte_without_overrun_regression()
        {
            using var tracker = new PNetMeshPacketTracker();

            for (ulong i = 0; i < 8; i++)
                Assert.True(tracker.TryAdd(i));

            var buffer = new byte[1];
            tracker.GetBitmap(0, buffer, out var bytesUsed);

            Assert.Equal(1, bytesUsed);
            Assert.Equal(0xFF, buffer[0]);
        }

        [Fact]
        public void bitmap_from_small_end()
        {
            using var tracker = new PNetMeshPacketTracker();

            var counters = new ulong[] { 1, 29, 45, 49, 48, 46, 50 };

            for (int i = 0; i < counters.Length; i++)
                Assert.True(tracker.TryAdd(counters[i]));


            var buffer = new byte[8];

            tracker.GetBitmap(45, buffer.AsSpan().Slice(0, 1), out var bytesUsed);
            Assert.Equal(1, bytesUsed);

            var bits = new BitArray(buffer);
            Assert.True(bits.Get(0)); //45
            Assert.True(bits.Get(1)); //46
            Assert.False(bits.Get(2)); //47
            Assert.True(bits.Get(3)); //48
            Assert.True(bits.Get(4)); //49
            Assert.True(bits.Get(5)); //50
        }

        [Theory]
        [InlineData(4098)]
        [InlineData(2048)]
        [InlineData(1024)]
        public void bitmap_from_large_set(int counterSize)
        {
            using var tracker = new PNetMeshPacketTracker(counterSize);

            int i;
            for (i = 0; i < 5000; i += 3)
                Assert.True(tracker.TryAdd((ulong)i));

            var buffer = new byte[64];

            tracker.GetBitmap(4488, buffer, out var bytesUsed);
            Assert.Equal(64, bytesUsed);

            var bits = new BitArray(buffer);

            for (i = 0; 4488 + i < (int)tracker.Current; i++)
                Assert.Equal(i % 3 == 0, bits[i]);
        }

        [Fact]
        public void bitmap_right_shift()
        {
            var bitmap = new byte[] { 0x7F, 0x01 }; // 0000_0001 0111_1111

            var c = PNetMeshPacketTracker.RightShift(bitmap, out var bytesUsed);
            Assert.True(c == 7);
            Assert.True(bytesUsed == 1);
            Assert.True(bitmap[0] == 0x02);
            Assert.True(bitmap[1] == 0x00);


            bitmap = new byte[] { 0x7F, 0x01, 0x02 }; // 0000_0010 0000_0001 0111_1111

            c = PNetMeshPacketTracker.RightShift(bitmap, out bytesUsed);
            Assert.True(c == 7);
            Assert.True(bytesUsed == 2);
            Assert.True(bitmap[0] == 0x02);
            Assert.True(bitmap[1] == 0x04);
            Assert.True(bitmap[2] == 0x00);


            bitmap = new byte[] { 0xFF, 0xFF, 0x1b }; // 0001_1011 1111_1111 1111_1111

            c = PNetMeshPacketTracker.RightShift(bitmap, out bytesUsed);
            Assert.True(c == 18);
            Assert.True(bytesUsed == 1);
            Assert.True(bitmap[0] == 0x06);
            Assert.True(bitmap[1] == 0x00);
            Assert.True(bitmap[2] == 0x00);


            bitmap = new byte[] { 0xFF, 0xFF, 0x01 }; // 0000_0001 1111_1111 1111_1111

            c = PNetMeshPacketTracker.RightShift(bitmap, out bytesUsed);
            Assert.True(c == 17);
            Assert.True(bytesUsed == 0);
            Assert.True(bitmap[0] == 0x00);
            Assert.True(bitmap[1] == 0x00);
            Assert.True(bitmap[2] == 0x00);
        }

        [Fact]
        public void bitmap_right_shift_handles_exact_full_byte_regression()
        {
            var bitmap = new byte[] { 0xFF };

            var shifted = PNetMeshPacketTracker.RightShift(bitmap, out var bytesUsed);

            Assert.Equal(8u, shifted);
            Assert.Equal(0, bytesUsed);
            Assert.Equal(0x00, bitmap[0]);
        }
    }
}
