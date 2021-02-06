using Noise;
using System;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace PNet.Actor.UnitTests.Mesh
{
    public sealed class PNetMeshTest
    {
        readonly ITestOutputHelper _output;

        public PNetMeshTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void print_six_node_keys()
        {
            for (int i = 0; i < 6; i++)
            {
                using var key = KeyPair.Generate();

                _output.WriteLine($"Node[{i}]\nPublicKey: \"{Convert.ToBase64String(key.PublicKey)}\"\nPrivateKey: \"{Convert.ToBase64String(key.PrivateKey)}\"");
            }


            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            _output.WriteLine($"Psk: \"{Convert.ToBase64String(psk)}\"");
        }
    }
}
