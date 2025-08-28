using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ApiTestingApp;

public interface ILoadRunner
{
    Task RunAsync(string url, string? bootKey, HttpMethod method, int concurrency, int requestsPerThread,
        TimeSpan delayBetween);
}

public sealed class LoadRunner(IServiceProvider sp) : ILoadRunner
{
    public async Task RunAsync(string url, string? bootKey, HttpMethod method, int concurrency, int requestsPerThread,
        TimeSpan delayBetween)
    {
        using var scope = sp.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var http = factory.CreateClient("ApiClient");

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Stop watcher (press 'S' to stop)
        var stopWatcher = Task.Run(() =>
        {
            Console.WriteLine("Running... Press 'S' to stop and return to the menu.");
            while (!token.IsCancellationRequested)
            {
                if (Console.KeyAvailable && char.ToUpperInvariant(Console.ReadKey(true).KeyChar) == 'S')
                {
                    Console.WriteLine("Stopping...");
                    cts.Cancel();
                    break;
                }

                Thread.Sleep(50);
            }
        }, token);

        var started = DateTime.UtcNow;
        long success = 0, fail = 0;
        var errors = new ConcurrentQueue<string>();

        var workers = Enumerable.Range(1, concurrency).Select(MakeWorker).ToArray();

        try
        {
            var results = await Task.WhenAll(workers);
            foreach (var r in results)
            {
                Interlocked.Add(ref success, r[0]);
                Interlocked.Add(ref fail, r[1]);
            }
        }
        catch (OperationCanceledException)
        {
            /* user pressed S */
        }

        var elapsed = DateTime.UtcNow - started;
        var total = success + fail;
        var rps = elapsed.TotalSeconds > 0 ? total / elapsed.TotalSeconds : 0;
        var successRate = total > 0 ? (double)success / total * 100 : 0;

        Console.WriteLine();
        Console.WriteLine("==== Summary ====");
        Console.WriteLine($"Elapsed: {elapsed:g}");
        Console.WriteLine($"Success: {success}");
        Console.WriteLine($"Failed : {fail}");
        Console.WriteLine($"Total  : {total}");
        Console.WriteLine($"Success Rate : {successRate:F2}%");
        Console.WriteLine($"Requests/sec: {rps:F2}");

        if (!errors.IsEmpty)
        {
            Console.WriteLine("-- First few errors --");
            foreach (var e in errors.Take(10)) Console.WriteLine(e);
            if (errors.Count > 10) Console.WriteLine($"...and {errors.Count - 10} more");
        }

        return;

        Task<long[]> MakeWorker(int id) => Task.Run(async () =>
        {
            long ok = 0, bad = 0;

            for (int i = 1; i <= requestsPerThread && !token.IsCancellationRequested; i++)
            {
                try
                {
                    var (finalUrl, content) = BuildRequest(method, url, bootKey, id, i);

                    using var req = new HttpRequestMessage(method, finalUrl);
                    req.Content = content;

                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                    if (resp.IsSuccessStatusCode) Interlocked.Increment(ref ok);
                    else
                    {
                        Interlocked.Increment(ref bad);
                        var msg = await SafeRead(resp, token);
                        errors.Enqueue($"[{id}:{i}] HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} - {msg}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref bad);
                    errors.Enqueue($"[{id}:{i}] {ex.GetType().Name}: {ex.Message}");
                }

                if (i < requestsPerThread && delayBetween > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delayBetween, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            return new[] { ok, bad };
        }, token);
    }

    private static (string finalUrl, HttpContent? content) BuildRequest(HttpMethod method, string url, string? bootKey,
        int workerId, int idx)
    {
        // Build a common payload
        var payload = new RequestPayload(
            MeasurementId: $"{workerId:D3}-{idx:D5}",
            TimestampUtc: DateTime.UtcNow,
            Inputs: new { thread = workerId, index = idx },
            BootKey: bootKey // may be null/empty
        );

        // Methods that typically carry a body:
        bool methodSupportsBody =
            method == HttpMethod.Post ||
            method == HttpMethod.Put ||
            method == HttpMethod.Patch ||
            method == HttpMethod.Delete; // DELETE can carry a body though some servers ignore it

        if (methodSupportsBody)
        {
            // Send JSON body
            return (url, JsonContent.Create(payload));
        }

        // For GET/HEAD/OPTIONS — append bootKey as query string if provided
        var finalUrl = AppendQuery(url, "bootKey", bootKey);
        return (finalUrl, null);
    }

    private static string AppendQuery(string url, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return url;
        var sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }

    private static async Task<string> SafeRead(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return (await resp.Content.ReadAsStringAsync(ct)).Trim();
        }
        catch
        {
            return "";
        }
    }
}