using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace DatabaseContainers.ApiService;

public static class ApiEndpoints
{
    public static WebApplication MapTodosApi(this WebApplication app)
    {
        var todos = app.MapGroup("")
            .WithTags("Todos");

        todos.MapGet("/todos", async (NpgsqlConnection db) =>
        {
            const string sql = """
                SELECT Id, Title, IsComplete
                FROM Todos
                """;

            return await db.QueryAsync<Todo>(sql);
        })
        .WithSummary("List todos")
        .WithDescription("Returns every todo item from the PostgreSQL **Todos** database.")
        .Produces<IEnumerable<Todo>>();

        todos.MapGet("/todos/{id}", async (int id, NpgsqlConnection db) =>
        {
            const string sql = """
                SELECT Id, Title, IsComplete
                FROM Todos
                WHERE Id = @id
                """;

            return await db.QueryFirstOrDefaultAsync<Todo>(sql, new { id }) is { } todo
                ? Results.Ok(todo)
                : Results.NotFound();
        })
        .WithSummary("Get a todo by id")
        .WithDescription("Returns a single todo item from the PostgreSQL **Todos** database.")
        .Produces<Todo>()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    public static WebApplication MapCatalogApi(this WebApplication app)
    {
        var catalog = app.MapGroup("")
            .WithTags("Catalog");

        catalog.MapGet("/catalog", async (MySqlConnection db) =>
        {
            const string sql = """
                SELECT Id, Name, Description, Price
                FROM catalog
                """;

            return await db.QueryAsync<CatalogItem>(sql);
        })
        .WithSummary("List catalog items")
        .WithDescription("Returns every catalog item from the MySQL **Catalog** database.")
        .Produces<IEnumerable<CatalogItem>>();

        catalog.MapGet("/catalog/{id}", async (int id, MySqlConnection db) =>
        {
            const string sql = """
                SELECT Id, Name, Description, Price
                FROM catalog
                WHERE Id = @id
                """;

            return await db.QueryFirstOrDefaultAsync<CatalogItem>(sql, new { id }) is { } item
                ? Results.Ok(item)
                : Results.NotFound();
        })
        .WithSummary("Get a catalog item by id")
        .WithDescription("Returns a single catalog item from the MySQL **Catalog** database.")
        .Produces<CatalogItem>()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    public static WebApplication MapAddressBookApi(this WebApplication app)
    {
        var addressBook = app.MapGroup("")
            .WithTags("AddressBook");

        addressBook.MapGet("/addressbook", async (SqlConnection db) =>
        {
            const string sql = """
                SELECT Id, FirstName, LastName, Email, Phone
                FROM Contacts
                """;

            return await db.QueryAsync<Contact>(sql);
        })
        .WithSummary("List contacts")
        .WithDescription("Returns every contact from the SQL Server **AddressBook** database.")
        .Produces<IEnumerable<Contact>>();

        addressBook.MapGet("/addressbook/{id}", async (int id, SqlConnection db) =>
        {
            const string sql = """
                SELECT Id, FirstName, LastName, Email, Phone
                FROM Contacts
                WHERE Id = @id
                """;

            return await db.QueryFirstOrDefaultAsync<Contact>(sql, new { id }) is { } contact
                ? Results.Ok(contact)
                : Results.NotFound();
        })
        .WithSummary("Get a contact by id")
        .WithDescription("Returns a single contact from the SQL Server **AddressBook** database.")
        .Produces<Contact>()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}

public record Todo(int Id, string Title, bool IsComplete);

public record CatalogItem(int Id, string Name, string Description, decimal Price);

public record Contact(int Id, string FirstName, string LastName, string Email, string? Phone);
