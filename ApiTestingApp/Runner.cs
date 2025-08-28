using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ApiTestingApp;

public interface ILoadRunner
{
    Task RunAsync(string url, string? bootKey, int concurrency, int requestsPerThread, TimeSpan delayBetween);
}

public sealed class LoadRunner(IServiceProvider sp) : ILoadRunner
{
    public async Task RunAsync(string url, string? bootKey, int concurrency, int requestsPerThread,
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
        Console.WriteLine();
        Console.WriteLine("==== Summary ====");
        Console.WriteLine($"Elapsed: {elapsed:g}");
        Console.WriteLine($"Success: {success}");
        Console.WriteLine($"Failed : {fail}");
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
                    var payload = new RequestPayload(
                        MeasurementId: $"{id:D3}-{i:D5}",
                        TimestampUtc: DateTime.UtcNow,
                        Inputs: new { thread = id, index = i },
                        BootKey: bootKey // may be null/empty
                    );

                    using var resp = await http.PostAsJsonAsync(url, payload, token);
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