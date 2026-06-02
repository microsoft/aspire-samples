using CustomResources.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddTalkingClock("talking-clock");

builder.AddTestResource("test");

builder.OnBeforeStart(static (@event, cancellationToken) =>
{
    var logger = @event.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("CustomResources.AppHost");

    logger.LogInformation("Starting custom resources sample with {ResourceCount} resources.", @event.Model.Resources.Count);

    return Task.CompletedTask;
});

builder.Eventing.Subscribe<AfterResourcesCreatedEvent>(static (@event, cancellationToken) =>
{
    var logger = @event.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("CustomResources.AppHost");

    logger.LogInformation("Custom resources sample created {ResourceCount} resources.", @event.Model.Resources.Count);

    return Task.CompletedTask;
});

builder.Build().Run();
