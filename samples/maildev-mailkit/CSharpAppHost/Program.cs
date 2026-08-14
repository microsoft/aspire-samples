using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var maildev = builder.AddMailDev("maildev");

builder.AddProject("newsletterservice", "../NewsletterService/NewsletterService.csproj")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(maildev)
    .WaitFor(maildev);

builder.Build().Run();