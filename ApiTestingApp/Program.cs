using System.Net;
using ApiTestingApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(_ =>
    {
        // appsettings.json is loaded by default; this is here for clarity
    })
    .ConfigureServices((ctx, services) =>
    {
        // Bind options from configuration
        services.Configure<ApiOptions>(ctx.Configuration.GetSection("Api"));

        // HttpClient via IHttpClientFactory
        services.AddHttpClient("ApiClient")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 1024,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

        // App services
        services.AddSingleton<ILoadRunner, LoadRunner>();
        services.AddSingleton<IConsoleMenu, ConsoleMenu>();
    })
    .Build();

var menu = host.Services.GetRequiredService<IConsoleMenu>();
await menu.RunAsync();