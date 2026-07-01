using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace PNet.Mesh.TestNode
{



    class Program
    {
        static async Task Main(string[] args)
        {
            using var host = CreateHostBuilder(args).Build();

            //await host.StartAsync();

            //await host.WaitForShutdownAsync();

            await host.RunAsync();

            Console.WriteLine("shutting down ...");

            await host.StopAsync();
        }

        static IHostBuilder CreateHostBuilder(string[] args)
            => Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOptions<NodeOptions>()
                        .Bind(hostContext.Configuration);

                    if (string.Equals(hostContext.Configuration["Mode"], "WireGuardPeer", StringComparison.OrdinalIgnoreCase))
                        services.AddHostedService<WireGuardPeerService>();
                    else if (string.Equals(hostContext.Configuration["Mode"], "WireGuardRelay", StringComparison.OrdinalIgnoreCase))
                        services.AddHostedService<WireGuardRelayService>();
                    else
                        services.AddHostedService<NodeService>();
                });
    }
}
