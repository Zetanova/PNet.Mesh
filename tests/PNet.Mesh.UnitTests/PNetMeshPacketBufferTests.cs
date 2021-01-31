using PNet.Actor.Mesh;
using System.Linq;
using Xunit;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshPacketBufferTests
    {
        [Fact]
        public void ack_first_buffer_and_get_out_of_order_seq()
        {
            var buffer = new PNetMeshPacketBuffer();

            for (int i = 0; i < 20; i++)
            {
                var m = buffer.Rent(128);
                Assert.Equal(i, (int)buffer.Current);
                m.Span[0] = (byte)i;
            }

            //0x19 0001_1001 => 10, 13, 14
            var seq = buffer.GetSequence(new byte[] { 0x19 }).ToList();
            Assert.True(seq.Count == 3);
            Assert.True(seq[0].Span[0] == 0);
            Assert.True(seq[1].Span[0] == 3);
            Assert.True(seq[2].Span[0] == 4);

            buffer.RemoveUntil(0); //remove 0-0
            Assert.Equal(1u, buffer.Latest);

            //0x19 0001_1001 => 10, 13, 14
            seq = buffer.GetSequence(new byte[] { 0x19 }).ToList();
            Assert.True(seq.Count == 3);
            Assert.True(seq[0].Span[0] == 1);
            Assert.True(seq[1].Span[0] == 4);
            Assert.True(seq[2].Span[0] == 5);
        }

        [Fact]
        public void ack_buffer_and_get_out_of_order_seq()
        {
            var buffer = new PNetMeshPacketBuffer();

            for (int i = 0; i < 20; i++)
            {
                var m = buffer.Rent(128);
                Assert.Equal(i, (int)buffer.Current);
                m.Span[0] = (byte)i;
            }

            buffer.RemoveUntil(9); //remove 0-9
            Assert.Equal(10u, buffer.Latest);

            //0x19 0001_1001 => 10, 13, 14
            var seq = buffer.GetSequence(new byte[] { 0x19 }).ToList();
            Assert.True(seq.Count == 3);
            Assert.True(seq[0].Span[0] == 10);
            Assert.True(seq[1].Span[0] == 13);
            Assert.True(seq[2].Span[0] == 14);


            buffer.RemoveUntil(10); //remove 10-10
            Assert.Equal(11u, buffer.Latest);

            //0x19 0001_1001 => 11, 14, 15
            seq = buffer.GetSequence(new byte[] { 0x19 }).ToList();
            Assert.True(seq.Count == 3);
            Assert.True(seq[0].Span[0] == 11);
            Assert.True(seq[1].Span[0] == 14);
            Assert.True(seq[2].Span[0] == 15);

            buffer.RemoveUntil(19); //remove 11-19
            Assert.Equal(0, buffer.Count);
            Assert.Equal(20u, buffer.Latest);

            for (int i = 20; i < 40; i++)
            {
                var m = buffer.Rent(128);
                Assert.Equal(i, (int)buffer.Current);
                m.Span[0] = (byte)i;
            }

            //0x19 0001_1001 => 20, 23, 24
            seq = buffer.GetSequence(new byte[] { 0x19 }).ToList();
            Assert.True(seq.Count == 3);
            Assert.True(seq[0].Span[0] == 20);
            Assert.True(seq[1].Span[0] == 23);
            Assert.True(seq[2].Span[0] == 24);
        }
    }
}
