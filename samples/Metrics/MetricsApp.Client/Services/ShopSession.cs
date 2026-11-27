namespace MetricsApp.Client.Services;

/// <summary>The shopper's cart and checkout state. Kept for the app's lifetime so it survives signing in.</summary>
public sealed class ShopSession(CheckoutClient client)
{
    public const int MaxQuantity = 10;

    public List<Product>? Products { get; private set; }

    public Cart? Cart { get; private set; }

    public OrderConfirmation? LastOrder { get; private set; }

    public string PaymentMethod { get; set; } = "card";

    public string ShippingMethod { get; set; } = "standard";

    public string? Error { get; private set; }

    public bool IsPlacingOrder { get; private set; }

    public int ItemCount => Cart?.ItemCount ?? 0;

    public event Action? Changed;

    public async Task LoadProductsAsync()
    {
        try
        {
            Products ??= await client.GetProductsAsync();
        }
        catch (HttpRequestException ex)
        {
            Error = $"Couldn't load the catalog: {ex.Message}";
        }
    }

    public int QuantityOf(string sku) => Cart?.Items.FirstOrDefault(i => i.Sku == sku)?.Quantity ?? 0;

    public Task AddAsync(Product product) => SetQuantityAsync(product.Sku, Math.Min(QuantityOf(product.Sku) + 1, MaxQuantity));

    public async Task SetQuantityAsync(string sku, int quantity)
    {
        Error = null;
        LastOrder = null;

        try
        {
            Cart ??= await client.CreateCartAsync();

            // The cart is gone if the service restarted; start a new one
            var updated = await client.SetQuantityAsync(Cart.Id, sku, quantity);
            if (updated is null)
            {
                Cart = await client.CreateCartAsync();
                updated = await client.SetQuantityAsync(Cart.Id, sku, quantity);
            }

            Cart = updated;
        }
        catch (HttpRequestException ex)
        {
            Error = $"Couldn't update the cart: {ex.Message}";
        }

        Changed?.Invoke();
    }

    public async Task PlaceOrderAsync()
    {
        if (Cart is not { ItemCount: > 0 })
        {
            return;
        }

        IsPlacingOrder = true;
        Error = null;
        Changed?.Invoke();

        try
        {
            var (order, error) = await client.CheckoutAsync(Cart.Id, PaymentMethod, ShippingMethod);
            if (order is not null)
            {
                LastOrder = order;
                Cart = null;
            }
            else
            {
                Error = error;
            }
        }
        catch (HttpRequestException ex)
        {
            Error = $"Checkout failed: {ex.Message}";
        }
        finally
        {
            IsPlacingOrder = false;
            Changed?.Invoke();
        }
    }

    public void StartNewOrder()
    {
        LastOrder = null;
        Changed?.Invoke();
    }
}
