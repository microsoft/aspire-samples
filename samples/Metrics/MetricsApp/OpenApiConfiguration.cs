using System.Text.Json.Nodes;
using MetricsApp.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace MetricsApp;

internal static class OpenApiConfiguration
{
    public const string Title = "Contoso Outfitters Checkout API";

    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddCheckoutOpenApi(this IServiceCollection services) => services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = Title,
                Version = "v1",
                Description = """
                    The checkout service behind the Contoso Outfitters store. Browse the catalog, fill a cart, and place an order.
                    Every checkout records OpenTelemetry metrics (`checkout.orders`, `checkout.order.value`, `checkout.duration`,
                    `checkout.carts.active`) that Aspire shows in its dashboard and the OpenTelemetry Collector exports to Prometheus and Grafana.
                    Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                    """
            };
            document.Tags = new HashSet<OpenApiTag>
            {
                new() { Name = "Catalog", Description = "Products that can be added to a cart." },
                new() { Name = "Carts", Description = "Anonymous shopping carts." },
                new() { Name = "Checkout", Description = "Turn a cart into an order. Requires a signed-in customer." },
                new() { Name = "Orders", Description = "Live checkout totals and the latest checkout attempts." },
                new() { Name = "Account", Description = "ASP.NET Core Identity endpoints. In development, sign in with `test@contoso.com` / `Password1!` to get a bearer token." }
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
            {
                [BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "The `accessToken` returned by `POST /api/login`."
                }
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
            {
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [] }];
            }
            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            var line = new JsonObject
            {
                ["sku"] = "summit-45-backpack",
                ["name"] = "Summit 45L Backpack",
                ["unitPrice"] = 149.00m,
                ["quantity"] = 1,
                ["lineTotal"] = 149.00m
            };

            if (type == typeof(Product))
            {
                schema.Examples = [new JsonObject
                {
                    ["sku"] = "summit-45-backpack",
                    ["name"] = "Summit 45L Backpack",
                    ["category"] = "Packs",
                    ["price"] = 149.00m,
                    ["description"] = "Lightweight trekking pack with an adjustable torso and a removable lid."
                }];
            }
            else if (type == typeof(CartDto))
            {
                schema.Examples = [new JsonObject
                {
                    ["id"] = "6f1c2a9e-3b7d-4c55-9a8e-2d4f0b1c7e93",
                    ["items"] = new JsonArray(line.DeepClone()),
                    ["itemCount"] = 1,
                    ["subtotal"] = 149.00m,
                    ["updatedAt"] = "2026-11-26T18:42:07Z"
                }];
            }
            else if (type == typeof(CartItemUpdate))
            {
                schema.Examples = [new JsonObject { ["quantity"] = 2 }];
            }
            else if (type == typeof(CheckoutRequest))
            {
                schema.Examples = [new JsonObject { ["paymentMethod"] = "card", ["shippingMethod"] = "express" }];
            }
            else if (type == typeof(OrderConfirmation))
            {
                schema.Examples = [new JsonObject
                {
                    ["orderId"] = "ORD-7F3A9C21",
                    ["items"] = new JsonArray(line.DeepClone()),
                    ["itemCount"] = 1,
                    ["subtotal"] = 149.00m,
                    ["shipping"] = 19.95m,
                    ["tax"] = 12.29m,
                    ["total"] = 181.24m,
                    ["paymentMethod"] = "card",
                    ["shippingMethod"] = "express",
                    ["placedAt"] = "2026-11-26T18:43:15Z"
                }];
            }
            return Task.CompletedTask;
        });
    });
}
