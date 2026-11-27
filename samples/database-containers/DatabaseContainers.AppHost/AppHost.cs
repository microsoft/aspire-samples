var builder = DistributedApplication.CreateBuilder(args);

// Contoso Outfitters keeps each part of its business in a different database container, and each container
// creates and seeds its own database on first start, without an ORM or a migration step:
// the product catalog lives in PostgreSQL, customer accounts in MySQL, and orders in SQL Server.

// The PostgreSQL container is configured with an auto-generated password by default
// and supports setting the default database name via an environment variable & running *.sql/*.sh init scripts.
var catalogDbName = "catalog";

var postgres = builder.AddPostgres("postgres")
    // Set the name of the default database to auto-create on container startup.
    .WithEnvironment("POSTGRES_DB", catalogDbName)
    // Copy the init scripts into /docker-entrypoint-initdb.d so that they create & seed the products table.
    .WithInitFiles("../DatabaseContainers.ApiService/data/postgres")
    // Configure the container to store data in a volume so that it persists across instances.
    .WithDataVolume()
    .WithPgWeb()
    // Keep the container running between app host sessions.
    .WithLifetime(ContainerLifetime.Persistent);

// Add the default database to the application model so that it can be referenced by other resources.
var catalogDb = postgres.AddDatabase(catalogDbName);

// The MySQL container is configured with an auto-generated password by default
// and supports setting the default database name via an environment variable & running *.sql/*.sh init scripts.
var customersDbName = "customers"; // MySQL database & table names are case-sensitive on non-Windows.

var mysql = builder.AddMySql("mysql")
    // Set the name of the database to auto-create on container startup.
    .WithEnvironment("MYSQL_DATABASE", customersDbName)
    // Copy the init scripts into /docker-entrypoint-initdb.d so that they create & seed the customers table.
    .WithInitFiles("../DatabaseContainers.ApiService/data/mysql")
    // Configure the container to store data in a volume so that it persists across instances.
    .WithDataVolume()
    // Keep the container running between app host sessions.
    .WithLifetime(ContainerLifetime.Persistent);

// Add the database to the application model so that it can be referenced by other resources.
var customersDb = mysql.AddDatabase(customersDbName);

// The SQL Server container is configured with an auto-generated password by default
// but doesn't support any auto-creation of databases or running scripts on startup,
// so Aspire runs the init script as the database's creation script instead.
var sqlserver = builder.AddSqlServer("sqlserver")
    // Configure the container to store data in a volume so that it persists across instances.
    .WithDataVolume()
    // Keep the container running between app host sessions.
    .WithLifetime(ContainerLifetime.Persistent);

// Add the database to the application model so that it can be referenced by other resources.
var initScriptPath = Path.Join(Path.GetDirectoryName(typeof(Program).Assembly.Location), "init.sql");
var ordersDb = sqlserver.AddDatabase("orders")
    .WithCreationScript(File.ReadAllText(initScriptPath));

var api = builder.AddDotnetProject("apiservice", "../DatabaseContainers.ApiService/DatabaseContainers.ApiService.csproj")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(customersDb)
    .WaitFor(customersDb)
    .WithReference(ordersDb)
    .WaitFor(ordersDb)
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        // Surface the API reference (mapped in Development only) and keep the raw endpoint URLs on the details page
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }

        var endpoint = context.GetEndpoint("https") is { Exists: true } https ? https : context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    });

if (builder.ExecutionContext.IsRunMode)
{
    // Both commands call development-only API endpoints
    api.WithHttpCommand("/dev/simulate-orders", "Simulate orders", commandName: "simulate-orders", commandOptions: new()
    {
        Description = "Places 24 orders for random customers. Each order reads the customer from MySQL and the prices from PostgreSQL, then saves the order to SQL Server.",
        IconName = "Cart",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    });

    api.WithHttpCommand("/dev/reset-orders", "Reset orders", commandName: "reset-orders", commandOptions: new()
    {
        Description = "Drops the orders tables and re-runs the SQL Server init script, so the order history returns to its seeded state.",
        ConfirmationMessage = "Delete every order placed since the orders database was seeded?",
        IconName = "DatabaseLightning",
        ResultMode = HttpCommandResultMode.Json
    });
}

builder.Build().Run();
