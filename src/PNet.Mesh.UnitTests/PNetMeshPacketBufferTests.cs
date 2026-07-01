using PNet.Mesh;
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

        [Fact]
        public void untracked_holes_do_not_count_as_outstanding_or_retransmittable()
        {
            var buffer = new PNetMeshPacketBuffer();

            var first = buffer.Rent(128);
            first.Span[0] = 10;
            Assert.Equal(0u, buffer.Current);

            buffer.AddUntracked(1);
            Assert.Equal(1u, buffer.Current);

            var second = buffer.Rent(128);
            second.Span[0] = 12;
            Assert.Equal(2u, buffer.Current);

            Assert.Equal(2, buffer.Count);
            Assert.Equal(2, buffer.CountOutstanding(0));
            Assert.Equal(1, buffer.CountOutstanding(1));
            Assert.Equal(0, buffer.CountOutstanding(3));

            var sequence = buffer.GetSequence().ToList();
            Assert.Equal(2, sequence.Count);
            Assert.Equal(10, sequence[0].Span[0]);
            Assert.Equal(12, sequence[1].Span[0]);

            var missing = buffer.GetMissingSequence(new byte[] { 0x00 }).ToList();
            Assert.Equal(2, missing.Count);
            Assert.Equal(10, missing[0].Span[0]);
            Assert.Equal(12, missing[1].Span[0]);

            buffer.RemoveUntil(1);
            Assert.Equal(2u, buffer.Latest);
            Assert.Equal(1, buffer.Count);

            sequence = buffer.GetSequence().ToList();
            Assert.Single(sequence);
            Assert.Equal(12, sequence[0].Span[0]);

            buffer.RemoveUntil(2);
            Assert.Equal(0, buffer.Count);
            Assert.Equal(3u, buffer.Latest);
        }
    }
}
