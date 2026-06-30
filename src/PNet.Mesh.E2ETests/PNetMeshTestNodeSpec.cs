using System.Collections.Generic;

namespace PNet.Actor.E2ETests.Mesh;

public sealed class PNetMeshTestNodeSpec
{
    public required string Name { get; init; }

    public required string PublicKey { get; init; }

    public required string PrivateKey { get; init; }

    public required string Psk { get; init; }

    public int Port { get; init; } = 12401;

    public string BindAddress { get; init; } = "0.0.0.0";

    public int ConnectDelaySeconds { get; init; }

    public int RunDurationSeconds { get; init; } = 120;

    public IReadOnlyDictionary<string, string> ToEnvironment()
    {
        return new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["NodeName"] = Name,
            ["PublicKey"] = PublicKey,
            ["PrivateKey"] = PrivateKey,
            ["Psk"] = Psk,
            ["BindTo__0"] = $"{BindAddress}:{Port}",
            ["ConnectDelaySeconds"] = ConnectDelaySeconds.ToString(),
            ["PingAllConnectedNodes"] = "false",
            ["RunDurationSeconds"] = RunDurationSeconds.ToString()
        };
    }

    public static PNetMeshTestNodeSpec StandaloneNode00()
    {
        return new PNetMeshTestNodeSpec
        {
            Name = "tc-node00",
            PublicKey = "zE/XdpVKkCnoNElMYntOQ043bXvc5x9K4jeyg+uZbjg=",
            PrivateKey = "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            Psk = "lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4="
        };
    }

    public static PNetMeshTestNodeSpec InvalidBindNode()
    {
        return new PNetMeshTestNodeSpec
        {
            Name = "tc-invalid-bind",
            PublicKey = "zE/XdpVKkCnoNElMYntOQ043bXvc5x9K4jeyg+uZbjg=",
            PrivateKey = "H+wvAlb/Q+pKX2z9l5qJpD+ikXm+6pxJQtrp69ZkyYI=",
            Psk = "lBeIat8LnqvYKJ7hZBIysLwkUbxLoTfSYzq+wIDENa4=",
            BindAddress = "not-an-ip-address"
        };
    }
}
