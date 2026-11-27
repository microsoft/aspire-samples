using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace DatabaseContainers.ApiService;

// Dapper maps each row to a record through its constructor, so the column order in each SELECT matches the record's parameters.

/// <summary>Queries for the product catalog in the PostgreSQL <c>catalog</c> database.</summary>
internal static class CatalogDb
{
    public static async Task<List<Product>> GetProductsAsync(this NpgsqlConnection db, string? category, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, sku, name, category, description, price, units_in_stock AS UnitsInStock
            FROM products
            WHERE @category::text IS NULL OR category = @category
            ORDER BY id
            """;

        return (await db.QueryAsync<Product>(new CommandDefinition(sql, new { category }, cancellationToken: cancellationToken))).AsList();
    }

    public static Task<Product?> FindProductAsync(this NpgsqlConnection db, int id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, sku, name, category, description, price, units_in_stock AS UnitsInStock
            FROM products
            WHERE id = @id
            """;

        return db.QuerySingleOrDefaultAsync<Product?>(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));
    }

    public static async Task<Dictionary<int, Product>> FindProductsAsync(this NpgsqlConnection db, int[] ids, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, sku, name, category, description, price, units_in_stock AS UnitsInStock
            FROM products
            WHERE id = ANY(@ids)
            """;

        var products = await db.QueryAsync<Product>(new CommandDefinition(sql, new { ids }, cancellationToken: cancellationToken));
        return products.ToDictionary(p => p.Id);
    }
}

/// <summary>Queries for customer accounts in the MySQL <c>customers</c> database.</summary>
internal static class CustomersDb
{
    public static async Task<List<Customer>> GetCustomersAsync(this MySqlConnection db, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, first_name AS FirstName, last_name AS LastName, email, city, country, loyalty_tier AS LoyaltyTier
            FROM customers
            ORDER BY id
            """;

        return (await db.QueryAsync<Customer>(new CommandDefinition(sql, cancellationToken: cancellationToken))).AsList();
    }

    public static Task<Customer?> FindCustomerAsync(this MySqlConnection db, int id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, first_name AS FirstName, last_name AS LastName, email, city, country, loyalty_tier AS LoyaltyTier
            FROM customers
            WHERE id = @id
            """;

        return db.QuerySingleOrDefaultAsync<Customer?>(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));
    }
}

/// <summary>Queries for orders in the SQL Server <c>orders</c> database.</summary>
internal static partial class OrdersDb
{
    public static async Task<List<OrderSummary>> GetOrdersAsync(this SqlConnection db, int? customerId, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (@limit) o.Id, o.CustomerId, o.PlacedAt, o.Status,
                   (SELECT ISNULL(SUM(l.Quantity), 0) FROM dbo.OrderLines l WHERE l.OrderId = o.Id) AS ItemCount,
                   o.Total
            FROM dbo.Orders o
            WHERE @customerId IS NULL OR o.CustomerId = @customerId
            ORDER BY o.PlacedAt DESC
            """;

        return (await db.QueryAsync<OrderSummary>(new CommandDefinition(sql, new { customerId, limit }, cancellationToken: cancellationToken))).AsList();
    }

    public static async Task<(OrderSummary Order, List<OrderLine> Lines)?> FindOrderAsync(this SqlConnection db, int id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT o.Id, o.CustomerId, o.PlacedAt, o.Status,
                   (SELECT ISNULL(SUM(l.Quantity), 0) FROM dbo.OrderLines l WHERE l.OrderId = o.Id) AS ItemCount,
                   o.Total
            FROM dbo.Orders o
            WHERE o.Id = @id;

            SELECT ProductId, ProductName, Quantity, UnitPrice
            FROM dbo.OrderLines
            WHERE OrderId = @id
            ORDER BY ProductName;
            """;

        using var results = await db.QueryMultipleAsync(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));
        var order = await results.ReadSingleOrDefaultAsync<OrderSummary>();
        return order is null ? null : (order, (await results.ReadAsync<OrderLine>()).AsList());
    }

    public static async Task<(int Id, DateTimeOffset PlacedAt)> InsertOrderAsync(
        this SqlConnection db, int customerId, IReadOnlyList<OrderLine> lines, decimal total, CancellationToken cancellationToken)
    {
        await db.OpenAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        const string insertOrder = """
            INSERT INTO dbo.Orders (CustomerId, Total)
            OUTPUT INSERTED.Id, INSERTED.PlacedAt
            VALUES (@customerId, @total)
            """;

        var order = await db.QuerySingleAsync<(int Id, DateTimeOffset PlacedAt)>(
            new CommandDefinition(insertOrder, new { customerId, total }, transaction, cancellationToken: cancellationToken));

        const string insertLines = """
            INSERT INTO dbo.OrderLines (OrderId, ProductId, ProductName, Quantity, UnitPrice)
            SELECT @orderId, ProductId, ProductName, Quantity, UnitPrice
            FROM OPENJSON(@lines) WITH (ProductId INT, ProductName NVARCHAR(200), Quantity INT, UnitPrice DECIMAL(10, 2))
            """;

        // Insert every line in a single statement by passing the lines as a JSON array
        await db.ExecuteAsync(new CommandDefinition(
            insertLines,
            new { orderId = order.Id, lines = JsonSerializer.Serialize(lines) },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return order;
    }

    /// <summary>Drops the order tables and re-runs the database's init script, which recreates and re-seeds them.</summary>
    public static async Task<(int Batches, int Orders)> ResetAsync(this SqlConnection db, string initScript, CancellationToken cancellationToken)
    {
        // "GO" is a batch separator understood by SQL tools, not by SQL Server itself
        var batches = GoSeparator().Split(initScript).Where(batch => !string.IsNullOrWhiteSpace(batch)).ToArray();

        await db.OpenAsync(cancellationToken);
        await db.ExecuteAsync(new CommandDefinition("DROP TABLE IF EXISTS dbo.OrderLines, dbo.Orders", cancellationToken: cancellationToken));

        foreach (var batch in batches)
        {
            await db.ExecuteAsync(new CommandDefinition(batch, cancellationToken: cancellationToken));
        }

        var orders = await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.Orders", cancellationToken: cancellationToken));
        return (batches.Length, orders);
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GoSeparator();
}
