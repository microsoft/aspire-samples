using Grpc.Core;
using Polly.Timeout;
using AspireShop.GrpcBasket;
using AspireShop.BasketService.Models;

namespace AspireShop.Frontend.Services;

public class BasketServiceClient(Basket.BasketClient client)
{
    public async Task<(CustomerBasket? CustomerBasket, bool IsAvailable)> GetBasketAsync(string buyerId)
    {
        try
        {
            var response = await client.GetBasketByIdAsync(new BasketRequest { Id = buyerId });
            var result = !string.IsNullOrEmpty(response.BuyerId) ? MapToCustomerBasket(response) : null;
            return (result, true);
        }
        catch (RpcException ex) when (
            // Service name could not be resolved
            ex.StatusCode is StatusCode.Unavailable ||
            // Polly resilience timed out after retries
            (ex.StatusCode is StatusCode.Internal && ex.Status.DebugException is TimeoutRejectedException))
        {
            return (null, false);
        }
    }

    public async Task<CustomerBasket> AddToCartAsync(string buyerId, int productId)
    {
        var (basket, _) = await GetBasketAsync(buyerId);
        basket ??= new CustomerBasket(buyerId);
        var found = false;
        foreach (var item in basket.Items)
        {
            if (item.ProductId == productId)
            {
                ++item.Quantity;
                found = true;
                break;
            }
        }

        if (!found)
        {
            basket.Items.Add(new BasketItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Quantity = 1,
                ProductId = productId
            });
        }

        var response = await client.UpdateBasketAsync(MapToCustomerBasketRequest(basket));
        var result = MapToCustomerBasket(response);
        return result;
    }

    public async Task CheckoutBasketAsync(string buyerId)
    {
        _ = await client.CheckoutBasketAsync(new CheckoutCustomerBasketRequest { BuyerId = buyerId });
    }

    public async Task DeleteBasketAsync(string buyerId)
    {
        _ = await client.DeleteBasketAsync(new DeleteCustomerBasketRequest { BuyerId = buyerId });
    }

    public async Task<CustomerBasket> AdjustItemQuantityAsync(string buyerId, int productId, int delta)
    {
        var (basket, _) = await GetBasketAsync(buyerId);
        basket ??= new CustomerBasket(buyerId);

        var item = basket.Items.FirstOrDefault(i => i.ProductId == productId);

        if (item is not null)
        {
            // Apply the change to the latest server-side quantity so rapid clicks or
            // a tampered form can't desync the count. A non-positive result removes the line.
            var newQuantity = item.Quantity + delta;
            if (newQuantity < 1)
            {
                basket.Items.Remove(item);
            }
            else
            {
                item.Quantity = Math.Min(newQuantity, MaxQuantityPerItem);
            }
        }

        var response = await client.UpdateBasketAsync(MapToCustomerBasketRequest(basket));
        return MapToCustomerBasket(response);
    }

    public async Task<CustomerBasket> RemoveItemAsync(string buyerId, int productId)
    {
        var (basket, _) = await GetBasketAsync(buyerId);
        basket ??= new CustomerBasket(buyerId);

        var item = basket.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item is not null)
        {
            basket.Items.Remove(item);
        }

        var response = await client.UpdateBasketAsync(MapToCustomerBasketRequest(basket));
        return MapToCustomerBasket(response);
    }

    private const int MaxQuantityPerItem = 99;

    private static CustomerBasketRequest MapToCustomerBasketRequest(CustomerBasket customerBasket)
    {
        var response = new CustomerBasketRequest
        {
            BuyerId = customerBasket.BuyerId
        };

        foreach (var item in customerBasket.Items)
        {
            response.Items.Add(new BasketItemResponse
            {
                Id = item.Id,
                OldUnitPrice = item.OldUnitPrice,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            });
        }

        return response;
    }

    private static CustomerBasket MapToCustomerBasket(CustomerBasketResponse wireBasket)
    {
        var response = new CustomerBasket
        {
            BuyerId = wireBasket.BuyerId
        };

        foreach (var item in wireBasket.Items)
        {
            response.Items.Add(new BasketItem
            {
                Id = item.Id,
                OldUnitPrice = item.OldUnitPrice,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            });
        }

        return response;
    }
}
