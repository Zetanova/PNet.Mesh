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

                    services.AddHostedService<NodeService>();
                });
    }
}
