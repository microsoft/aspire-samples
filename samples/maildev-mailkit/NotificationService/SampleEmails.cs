namespace NotificationService;

/// <summary>Realistic requests used for OpenAPI examples and the "Send sample emails" dashboard command.</summary>
public static class SampleEmails
{
    public static OrderConfirmationRequest Order { get; } = new()
    {
        Email = "maya.chen@example.com",
        CustomerName = "Maya Chen",
        OrderNumber = "CO-104582",
        Items =
        [
            new() { Sku = "TNT-RDG-2P", Name = "Ridgeline 2P Backpacking Tent", Quantity = 1, UnitPrice = 349.00m },
            new() { Sku = "JKT-SMT-W-M", Name = "Summit Down Jacket, Women's M", Quantity = 1, UnitPrice = 229.00m },
            new() { Sku = "SCK-MRN-3PK", Name = "Trailhead Merino Hiking Socks (3-pack)", Quantity = 2, UnitPrice = 36.00m },
        ],
        ShippingCost = 0m,
        Tax = 0m,
        ShipTo = new() { Line1 = "2217 SE Hawthorne Blvd", City = "Portland", Region = "OR", PostalCode = "97214" }
    };

    public static PasswordResetRequest PasswordReset { get; } = new()
    {
        Email = "daniel.okafor@example.net",
        CustomerName = "Daniel Okafor",
        ExpiresInMinutes = 30
    };

    public static ShippingNotificationRequest Shipment { get; } = new()
    {
        Email = "maya.chen@example.com",
        CustomerName = "Maya Chen",
        OrderNumber = "CO-104582",
        Carrier = Carrier.Ups,
        TrackingNumber = "1Z999AA10123456784",
        EstimatedDelivery = new DateOnly(2026, 11, 27),
        ItemCount = 4
    };

    /// <summary>A morning's worth of storefront activity across every template.</summary>
    public static IEnumerable<object> Batch(DateOnly today) =>
    [
        Order,
        new OrderConfirmationRequest
        {
            Email = "lucas.romero@example.org",
            CustomerName = "Lucas Romero",
            OrderNumber = "CO-104597",
            Items =
            [
                new() { Sku = "PCK-CSC-30L", Name = "Cascade 30L Daypack", Quantity = 1, UnitPrice = 119.00m },
                new() { Sku = "BTL-INS-32", Name = "Basecamp Insulated Bottle, 32 oz", Quantity = 2, UnitPrice = 34.95m },
            ],
            ShippingCost = 7.95m,
            Tax = 15.73m,
            ShipTo = new() { Line1 = "480 Larkin St", City = "San Francisco", Region = "CA", PostalCode = "94102" }
        },
        PasswordReset,
        new ShippingNotificationRequest
        {
            Email = "maya.chen@example.com",
            CustomerName = "Maya Chen",
            OrderNumber = "CO-104582",
            Carrier = Carrier.Ups,
            TrackingNumber = "1Z999AA10123456784",
            EstimatedDelivery = today.AddDays(3),
            ItemCount = 4
        },
        new ShippingNotificationRequest
        {
            Email = "priya.natarajan@example.com",
            CustomerName = "Priya Natarajan",
            OrderNumber = "CO-104511",
            Carrier = Carrier.FedEx,
            TrackingNumber = "794644790138",
            EstimatedDelivery = today.AddDays(2),
            ItemCount = 1
        },
        new PasswordResetRequest { Email = "sam.whitfield@example.org", CustomerName = "Sam Whitfield", ExpiresInMinutes = 15 },
    ];
}
