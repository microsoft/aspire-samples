using System.ComponentModel;

namespace DatabaseContainers.ApiService;

[Description("A product in the catalog, stored in PostgreSQL.")]
public record Product(
    [property: Description("The product ID.")] int Id,
    [property: Description("The stock-keeping unit.")] string Sku,
    [property: Description("The product name.")] string Name,
    [property: Description("The product category: `Packs`, `Apparel`, `Footwear`, `Camping`, or `Accessories`.")] string Category,
    [property: Description("A short description of the product.")] string Description,
    [property: Description("The unit price in USD.")] decimal Price,
    [property: Description("Units on hand in the warehouse. Orders for more units than this are rejected.")] int UnitsInStock);

[Description("A customer's loyalty tier.")]
public enum LoyaltyTier { Bronze, Silver, Gold }

[Description("A customer account, stored in MySQL.")]
public record Customer(
    [property: Description("The customer ID.")] int Id,
    [property: Description("The customer's first name.")] string FirstName,
    [property: Description("The customer's last name.")] string LastName,
    [property: Description("The customer's email address.")] string Email,
    [property: Description("The customer's city.")] string City,
    [property: Description("The customer's ISO 3166-1 alpha-2 country code.")] string Country,
    [property: Description("The customer's loyalty tier.")] LoyaltyTier LoyaltyTier);

[Description("Where an order is in fulfillment.")]
public enum OrderStatus { Placed, Packed, Shipped, Delivered }

[Description("An order, stored in SQL Server.")]
public record OrderSummary(
    [property: Description("The order ID.")] int Id,
    [property: Description("The ID of the customer who placed the order.")] int CustomerId,
    [property: Description("When the order was placed.")] DateTimeOffset PlacedAt,
    [property: Description("Where the order is in fulfillment.")] OrderStatus Status,
    [property: Description("The number of units in the order.")] int ItemCount,
    [property: Description("The order total in USD.")] decimal Total);

[Description("A line of an order. The product name and unit price are copied from the catalog when the order is placed.")]
public record OrderLine(
    [property: Description("The ID of the product in the catalog.")] int ProductId,
    [property: Description("The product name when the order was placed.")] string ProductName,
    [property: Description("The number of units ordered.")] int Quantity,
    [property: Description("The unit price in USD when the order was placed.")] decimal UnitPrice)
{
    [Description("The line total in USD.")]
    public decimal LineTotal => Quantity * UnitPrice;
}

[Description("An order with its lines (from SQL Server) and its customer (from MySQL).")]
public record OrderDetails(
    [property: Description("The order ID.")] int Id,
    [property: Description("When the order was placed.")] DateTimeOffset PlacedAt,
    [property: Description("Where the order is in fulfillment.")] OrderStatus Status,
    [property: Description("The ID of the customer who placed the order.")] int CustomerId,
    [property: Description("The customer who placed the order, or `null` if the account no longer exists.")] Customer? Customer,
    [property: Description("The ordered products.")] IReadOnlyList<OrderLine> Lines,
    [property: Description("The number of units in the order.")] int ItemCount,
    [property: Description("The order total in USD.")] decimal Total);

[Description("A request to place an order.")]
public record PlaceOrderRequest(
    [property: Description("The ID of the customer placing the order.")] int CustomerId,
    [property: Description("The products to order (1-10 lines, each product at most once).")] IReadOnlyList<PlaceOrderLine> Lines);

[Description("A product and quantity to order.")]
public record PlaceOrderLine(
    [property: Description("The ID of the product in the catalog.")] int ProductId,
    [property: Description("The number of units to order (1-10).")] int Quantity);
