#:package Aspire.Hosting.Redis@14.0.0-preview.1.26475.14
#:sdk Aspire.AppHost.Sdk@14.0.0-preview.1.26475.14
#:property AspireUseCliBundle=true

using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable ASPIRETERMINAL001

var builder = DistributedApplication.CreateBuilder(args);
var redis = builder.AddRedis("redis").WithRepl();
var api = builder.AddProject("api", "../basics-csharp/api/Notes.Api.csproj")
    .WithReference(redis)
    .WithHttpHealthCheck("/health")
    .WaitFor(redis);
var slumber = builder.AddDockerfile("slumber", "../basics-csharp/slumber")
    .WithBindMount("./requests.yml", "/home/slumber/slumber.yml", isReadOnly: true)
    .WithEnvironment("BASE_URL", api.GetEndpoint("http"))
    .WaitFor(api)
    .WithTerminal();

using var gate = new SemaphoreSlim(1);
if (builder.ExecutionContext.IsRunMode)
{
    slumber.WithCommand("walkthrough", "Run terminal walkthrough", async context =>
    {
        if (!await gate.WaitAsync(0, context.CancellationToken))
        {
            return CommandResults.Failure("A walkthrough is already running.");
        }

        var logger = context.Services.GetRequiredService<ResourceLoggerService>().GetLogger(slumber.Resource);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        try
        {
            var notifications = context.Services.GetRequiredService<ResourceNotificationService>();
            await notifications.WaitForResourceHealthyAsync(api.Resource.Name, ct);
            using var client = new HttpClient { BaseAddress = new Uri(api.GetEndpoint("http").Url) };
            // Reset only this walkthrough's note. All demonstrated CRUD operations go through the TUI.
            using var reset = await client.DeleteAsync("/notes/automation", ct);
            if (reset.StatusCode != HttpStatusCode.NotFound)
            {
                reset.EnsureSuccessStatusCode();
            }

            // A new container and non-persisted recipes prevent old responses from satisfying waits.
            if (!notifications.TryGetCurrentState(context.ResourceName, out var previous))
            {
                return CommandResults.Failure("Slumber has no running snapshot.");
            }
            var previousId = ContainerId(previous.Snapshot);
            var commands = context.Services.GetRequiredService<ResourceCommandService>();
            var restart = await commands.ExecuteCommandAsync(context.ResourceName, "restart", ct);
            if (!restart.Success)
            {
                return restart;
            }
            await notifications.WaitForResourceAsync(slumber.Resource.Name,
                e => e.Snapshot.State?.Text == KnownResourceStates.Running &&
                    ContainerId(e.Snapshot) is { Length: > 0 } id && id != previousId, ct);

            var terminals = context.Services.GetRequiredService<TerminalService>();
            if (!terminals.TryGetTerminal($"resource:{slumber.Resource.Name}:0", out var terminal))
            {
                return CommandResults.Failure("Slumber's terminal is not registered.");
            }
            await using (terminal)
            {
                await terminal.WaitForTextAsync("No request history", cancellationToken: ct);
                await terminal.SendKeyAsync(AspireTerminalKey.R, ct);
                await SendAndCheckAsync(terminal, client, "201 Created", "Created by terminal automation", ct);
                logger.LogInformation("Create: terminal returned 201 and API contains the new note.");

                await NextAsync(terminal, "read_note", ct);
                await SendAndCheckAsync(terminal, client, "200 OK", "Created by terminal automation", ct);
                logger.LogInformation("Read: terminal and API agree on the note text.");

                await NextAsync(terminal, "update_note", ct);
                await SendAndCheckAsync(terminal, client, "200 OK", "Updated by terminal automation", ct);
                logger.LogInformation("Update: terminal and API agree on the changed text.");

                await NextAsync(terminal, "delete_note", ct);
                await terminal.SendKeyAsync(AspireTerminalKey.Enter, ct);
                await terminal.WaitForTextAsync("204 No Content", cancellationToken: ct);
                using var deleted = await client.GetAsync("/notes/automation", ct);
                if (deleted.StatusCode != HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException($"Expected 404 after delete, got {(int)deleted.StatusCode}.");
                }
                logger.LogInformation("Walkthrough passed: create, read, update and delete through Slumber.");
            }
            return CommandResults.Success();
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or HttpRequestException or InvalidOperationException)
        {
            logger.LogError(ex, "Terminal walkthrough failed. Inspect Slumber's screen; rerun to start a fresh session.");
            return CommandResults.Failure(ex is OperationCanceledException ? "Walkthrough canceled or timed out." : ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }, new CommandOptions
    {
        Description = "Restarts Slumber and drives CRUD through its terminal. Do not type into Slumber during the run.",
        UpdateState = context => context.ResourceSnapshot.State?.Text == KnownResourceStates.Running
            ? ResourceCommandState.Enabled : ResourceCommandState.Disabled
    });
}

builder.Build().Run();

static string? ContainerId(CustomResourceSnapshot snapshot) =>
    snapshot.Properties.FirstOrDefault(p => p.Name == "container.id")?.Value as string;

static async Task NextAsync(AspireTerminal terminal, string recipe, CancellationToken ct)
{
    await terminal.SendKeyAsync(AspireTerminalKey.R, ct);
    await terminal.SendKeyAsync(AspireTerminalKey.Down, ct);
    await terminal.WaitForTextAsync(recipe, cancellationToken: ct);
    await terminal.WaitForTextAsync("No request history", cancellationToken: ct);
}

static async Task SendAndCheckAsync(AspireTerminal terminal, HttpClient client, string status, string text, CancellationToken ct)
{
    await terminal.SendKeyAsync(AspireTerminalKey.Enter, ct);
    await terminal.WaitForTextAsync(status, cancellationToken: ct);
    await terminal.WaitForTextAsync(text, cancellationToken: ct);
    var note = await client.GetFromJsonAsync<Note>("/notes/automation", ct);
    if (note is not { Id: "automation" } || note.Text != text)
    {
        throw new InvalidOperationException("The API did not return the text shown in Slumber.");
    }
}

record Note(string Id, string Text);
