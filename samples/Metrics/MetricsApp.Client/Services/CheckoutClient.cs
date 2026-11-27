using System.Net;
using System.Net.Http.Json;

namespace MetricsApp.Client.Services;

/// <summary>Calls the checkout service's HTTP API.</summary>
public sealed class CheckoutClient(HttpClient http)
{
    public async Task<List<Product>> GetProductsAsync() =>
        await http.GetFromJsonAsync<List<Product>>("api/catalog") ?? [];

    public Task<CheckoutOverview?> GetOverviewAsync() =>
        http.GetFromJsonAsync<CheckoutOverview>("api/orders/overview");

    public async Task<string?> GetGrafanaUrlAsync() =>
        (await http.GetFromJsonAsync<StartupConfig>("api/startup"))?.GrafanaUrl;

    public async Task<Cart> CreateCartAsync()
    {
        using var response = await http.PostAsync("api/carts", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Cart>())!;
    }

    /// <summary>Sets a product's quantity, or returns null when the cart no longer exists.</summary>
    public async Task<Cart?> SetQuantityAsync(Guid cartId, string sku, int quantity)
    {
        using var response = await http.PutAsJsonAsync($"api/carts/{cartId}/items/{sku}", new { quantity });
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Cart>();
    }

    public async Task<(OrderConfirmation? Order, string? Error)> CheckoutAsync(Guid cartId, string paymentMethod, string shippingMethod)
    {
        using var response = await http.PostAsJsonAsync($"api/carts/{cartId}/checkout", new { paymentMethod, shippingMethod });
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<OrderConfirmation>(), null);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return (null, "Your session expired. Sign in again to check out.");
        }

        var problem = response.Content.Headers.ContentType?.MediaType == "application/problem+json"
            ? await response.Content.ReadFromJsonAsync<ProblemDetails>()
            : null;
        return (null, problem?.Detail ?? $"Checkout failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
    }

    private sealed record StartupConfig(string? GrafanaUrl);
}
