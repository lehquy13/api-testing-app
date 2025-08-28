using Microsoft.Extensions.Options;

namespace ApiTestingApp;

public interface IConsoleMenu
{
    Task RunAsync();
}

public sealed class ConsoleMenu(ILoadRunner runner, IOptionsMonitor<ApiOptions> apiOptions) : IConsoleMenu
{
    public async Task RunAsync()
    {
        Console.Title = "HTTP Load Console (Key in Body, DI + HostBuilder)";
        while (true)
        {
            Console.Clear();
            Console.WriteLine("==== HTTP Load Console ====");
            Console.WriteLine("1) Start sending requests");
            Console.WriteLine("0) Exit");
            Console.Write("Select: ");
            var sel = Console.ReadLine();
            if (sel == "0") return;
            if (sel != "1") continue;

            // URL (default from config)
            var defaultUrl = apiOptions.CurrentValue.Url ?? string.Empty;
            Console.WriteLine($"API URL (from config): {defaultUrl}");
            Console.Write("Override URL? (leave blank to use default): ");
            var overrideUrl = Console.ReadLine()?.Trim();
            var url = string.IsNullOrWhiteSpace(overrideUrl) ? defaultUrl : overrideUrl;

            if (string.IsNullOrWhiteSpace(url))
            {
                Console.WriteLine("No URL provided. Press any key to return to menu...");
                Console.ReadKey(true);
                continue;
            }

            Console.Write("Key (bootKey) to include in request body (blank = will be logged): ");
            var key = Console.ReadLine();

            var concurrency = ReadInt("Concurrent threads [default 1]: ", 1, min: 1);
            var perThread = ReadInt("Requests per thread [default 1]: ", 1, min: 1);
            var delaySec = ReadInt("Delay between requests in seconds [default 30]: ", 30, min: 0);

            Console.WriteLine();
            Console.WriteLine($"Target: {url}");
            Console.WriteLine($"bootKey: {(string.IsNullOrWhiteSpace(key) ? "(empty)" : key)}");
            Console.WriteLine($"Threads: {concurrency}, Requests/Thread: {perThread}, Delay: {delaySec}s");
            Console.WriteLine("Press ENTER to start, or any other key to cancel...");
            if (Console.ReadKey(true).Key != ConsoleKey.Enter) continue;

            await runner.RunAsync(url, key, concurrency, perThread, TimeSpan.FromSeconds(delaySec));
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey(true);
        }
    }

    private static int ReadInt(string prompt, int @default, int min = int.MinValue, int max = int.MaxValue)
    {
        while (true)
        {
            Console.Write(prompt);
            var s = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(s)) return @default;
            if (int.TryParse(s, out var v) && v >= min && v <= max) return v;
            Console.WriteLine($"Please enter an integer between {min} and {max} (or leave blank for {@default}).");
        }
    }
}

public sealed class ApiOptions
{
    public string? Url { get; set; }
}

internal sealed record RequestPayload(
    string? MeasurementId,
    DateTime TimestampUtc,
    object? Inputs,
    string? BootKey
);