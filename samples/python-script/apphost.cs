#:package Aspire.Hosting.Python@13.2.2
#:sdk Aspire.AppHost.Sdk@13.2.2

var builder = DistributedApplication.CreateBuilder(args);

builder.AddPythonApp("script", "./script", "main.py");

builder.Build().Run();


