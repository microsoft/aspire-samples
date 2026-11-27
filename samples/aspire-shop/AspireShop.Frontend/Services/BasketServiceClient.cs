using System.Diagnostics;
using Grpc.Core;
using Polly.Timeout;
using AspireShop.GrpcBasket;
using AspireShop.BasketService.Models;

namespace AspireShop.Frontend.Services;

public class BasketServiceClient(Basket.BasketClient client, FrontendTelemetry telemetry, ILogger<BasketServiceClient> logger)
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

    public async Task<CustomerBasket> AddToCartAsync(string buyerId, CatalogItem catalogItem)
    {
        using var activity = FrontendTelemetry.ActivitySource.StartActivity("add to cart");
        activity?.SetTag("aspireshop.buyer.id", buyerId);
        activity?.SetTag("aspireshop.catalog.item.id", catalogItem.Id);

        var (basket, _) = await GetBasketAsync(buyerId);
        basket ??= new CustomerBasket(buyerId);

        // Capture the current catalog price so the Basket service can total the order at checkout
        if (basket.Items.FirstOrDefault(i => i.ProductId == catalogItem.Id) is { } item)
        {
            item.Quantity = Math.Min(item.Quantity + 1, MaxQuantityPerItem);
            item.UnitPrice = catalogItem.Price;
        }
        else
        {
            basket.Items.Add(new BasketItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Quantity = 1,
                ProductId = catalogItem.Id,
                UnitPrice = catalogItem.Price
            });
        }

        var response = await client.UpdateBasketAsync(MapToCustomerBasketRequest(basket));
        var result = MapToCustomerBasket(response);
        activity?.SetTag("aspireshop.basket.item_count", result.TotalItemCount);
        return result;
    }

    public async Task<OrderConfirmation?> CheckoutBasketAsync(string buyerId)
    {
        using var activity = FrontendTelemetry.ActivitySource.StartActivity("checkout");
        activity?.SetTag("aspireshop.buyer.id", buyerId);
        var start = Stopwatch.GetTimestamp();

        try
        {
            var response = await client.CheckoutBasketAsync(new CheckoutCustomerBasketRequest { BuyerId = buyerId });
            var order = new OrderConfirmation(response.OrderId, response.ItemCount, response.Total);
            var elapsed = Stopwatch.GetElapsedTime(start);

            activity?.SetTag("aspireshop.order.id", order.OrderId);
            activity?.SetTag("aspireshop.order.item_count", order.ItemCount);
            activity?.SetTag("aspireshop.order.total", (double)order.Total);
            telemetry.CheckoutFinished("completed", elapsed);
            logger.OrderPlaced(order.OrderId, buyerId, order.ItemCount, order.Total, elapsed.TotalMilliseconds);

            return order;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.FailedPrecondition)
        {
            // The basket was already checked out or emptied, e.g. from another tab
            activity?.SetTag("aspireshop.checkout.outcome", "rejected");
            telemetry.CheckoutFinished("rejected", Stopwatch.GetElapsedTime(start));
            logger.CheckoutRejected(buyerId, ex.Status.Detail);

            return null;
        }
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

public record OrderConfirmation(string OrderId, int ItemCount, decimal Total);
