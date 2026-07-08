using PNet.Mesh;
using System;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshVarintTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(127, 1)]
        [InlineData(128, 2)]
        [InlineData(255, 2)]
        [InlineData(300, 2)]
        [InlineData(16383, 2)]
        [InlineData(16384, 3)]
        [InlineData(int.MaxValue, 5)]
        [InlineData(-1, 5)]
        public void varint32_roundtrips_values_and_reports_size(int value, int expectedSize)
        {
            Span<byte> buffer = stackalloc byte[5];

            var bytesWritten = PNetMeshUtils.WriteVarint32(value, buffer);
            var actual = PNetMeshUtils.ReadVarint32(buffer[..bytesWritten], out var bytesRead);

            Assert.Equal(expectedSize, bytesWritten);
            Assert.Equal(expectedSize, bytesRead);
            Assert.Equal(expectedSize, PNetMeshUtils.GetVarint32Size(unchecked((uint)value)));
            Assert.Equal(value, actual);
        }

        [Fact]
        public void write_varint32_rejects_undersized_buffer()
        {
            var buffer = new byte[1];

            Assert.Throws<ArgumentException>(() => PNetMeshUtils.WriteVarint32(128, buffer));
        }

        [Fact]
        public void read_varint32_rejects_truncated_or_malformed_input()
        {
            Assert.Throws<InvalidOperationException>(() => PNetMeshUtils.ReadVarint32(new byte[] { 0x80 }, out _));
            Assert.Throws<InvalidOperationException>(() => PNetMeshUtils.ReadVarint32(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 }, out _));
        }
    }
}
