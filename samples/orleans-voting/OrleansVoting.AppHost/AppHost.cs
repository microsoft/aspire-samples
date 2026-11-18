var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("voting-redis");

var orleans = builder.AddOrleans("voting-cluster")
    .WithClustering(redis)
    .WithGrainStorage("votes", redis);

builder.AddDotnetProject("voting-fe", "../OrleansVoting.Service/OrleansVoting.Service.csproj")
    .WithReference(orleans)
    .WaitFor(redis)
    .WithReplicas(3)
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("https", u => u.DisplayText = "Voting App")
    .WithUrlForEndpoint("http", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly)
    .WithUrlForEndpoint("orleans-gateway", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly)
    .WithUrlForEndpoint("orleans-silo", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly);

builder.Build().Run();
