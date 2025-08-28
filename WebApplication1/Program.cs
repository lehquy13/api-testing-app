using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.IO;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

var sampleTodos = new Todo[]
{
    new(1, "Walk the dog"),
    new(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
    new(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
    new(4, "Clean the bathroom"),
    new(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
};

var todosApi = app.MapGroup("/todos");

long todoCallCount = 0;
todosApi.AddEndpointFilter(async (context, next) =>
{
    var count = Interlocked.Increment(ref todoCallCount);
    var request = context.HttpContext.Request;
    string body = string.Empty;
    if (request.ContentLength > 0)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
    }

    var dir = Path.Combine(AppContext.BaseDirectory, "RequestBodies");
    Directory.CreateDirectory(dir);
    var filePath = Path.Combine(dir, $"request-{count}.txt");
    await File.WriteAllTextAsync(filePath, body);

    app.Logger.LogInformation(
        "Call #{Count} {Method} {Path} Body: {Body}",
        count,
        request.Method,
        request.Path,
        body);

    return await next(context);
});

todosApi.MapGet("/", () => sampleTodos);
todosApi.MapGet("/{id}", (int id) =>
    sampleTodos.FirstOrDefault(a => a.Id == id) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

app.Run();

public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

[JsonSerializable(typeof(Todo[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}