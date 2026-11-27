using System.Diagnostics;
using Grpc.Core;
using AspireShop.GrpcBasket;
using AspireShop.BasketService.Models;
using AspireShop.BasketService.Repositories;

namespace AspireShop.BasketService;

public class BasketService(IBasketRepository repository, BasketTelemetry telemetry, ILogger<BasketService> logger)
    : Basket.BasketBase
{
    public override async Task<CustomerBasketResponse> GetBasketById(BasketRequest request, ServerCallContext context)
    {
        // Uncomment to force a delay for testing resiliency, etc.
        //await Task.Delay(3000);

        var data = await repository.GetBasketAsync(request.Id);

        if (data is not null)
        {
            return MapToCustomerBasketResponse(data);
        }

        return new CustomerBasketResponse();
    }

    public override async Task<CustomerBasketResponse> UpdateBasket(CustomerBasketRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A buyer id is required"));
        }

        using var activity = BasketTelemetry.ActivitySource.StartActivity("update basket");
        activity?.SetTag("aspireshop.buyer.id", request.BuyerId);

        var customerBasket = MapToCustomerBasket(request);

        // The write returns the basket it replaced, so the metrics describe this exact change even when
        // updates for the same buyer overlap (only one of them can find no previous basket and create it)
        var previous = await repository.UpdateBasketAsync(customerBasket);

        var (itemsAdded, itemsRemoved) = CompareQuantities(previous, customerBasket);
        telemetry.BasketChanged(created: previous is null, itemsAdded, itemsRemoved);

        activity?.SetTag("aspireshop.basket.item_count", customerBasket.TotalItemCount);
        activity?.AddEvent(new ActivityEvent("basket.changed", tags: new()
        {
            ["aspireshop.basket.items_added"] = itemsAdded,
            ["aspireshop.basket.items_removed"] = itemsRemoved
        }));
        logger.BasketUpdated(request.BuyerId, customerBasket.TotalItemCount, itemsAdded, itemsRemoved);

        return MapToCustomerBasketResponse(customerBasket);
    }

    public override async Task<CheckoutCustomerBasketResponse> CheckoutBasket(CheckoutCustomerBasketRequest request, ServerCallContext context)
    {
        var buyerId = request.BuyerId;

        using var activity = BasketTelemetry.ActivitySource.StartActivity("checkout basket");
        activity?.SetTag("aspireshop.buyer.id", buyerId);

        // Take the basket out of Redis in one step, so when checkouts of the same basket overlap only one of
        // them gets its items and the others find no basket and are rejected
        var basket = await repository.TakeBasketAsync(buyerId);

        if (basket is not { Items.Count: > 0 })
        {
            var outcome = basket is null ? "not_found" : "empty";
            activity?.SetTag("aspireshop.checkout.outcome", outcome);
            // An empty basket was taken too, which leaves the buyer with the same (empty) cart
            telemetry.CheckoutRejected(outcome, basketRemoved: basket is not null);
            logger.CheckoutRejected(buyerId, outcome);

            throw basket is null
                ? new RpcException(new Status(StatusCode.NotFound, $"Basket with buyer id {buyerId} does not exist"))
                : new RpcException(new Status(StatusCode.FailedPrecondition, $"Basket with buyer id {buyerId} is empty"));
        }

        activity?.AddEvent(new ActivityEvent("basket.taken"));

        var order = new Order
        {
            Id = $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            BuyerId = buyerId,
            Items = basket.Items,
        };

        activity?.SetTag("aspireshop.order.id", order.Id);
        activity?.SetTag("aspireshop.order.item_count", order.ItemCount);
        activity?.SetTag("aspireshop.order.total", (double)order.Total);
        activity?.AddEvent(new ActivityEvent("order.created"));

        // Payment and fulfillment would be implemented here

        activity?.SetTag("aspireshop.checkout.outcome", "completed");
        telemetry.CheckoutCompleted(order.Total);
        logger.OrderCreated(order.Id, buyerId, order.ItemCount, order.Total);

        var response = new CheckoutCustomerBasketResponse
        {
            OrderId = order.Id,
            ItemCount = order.ItemCount,
            Total = order.Total
        };
        response.Items.AddRange(order.Items.Select(MapToBasketItemResponse));

        return response;
    }

    public override async Task<DeleteCustomerBasketResponse> DeleteBasket(DeleteCustomerBasketRequest request, ServerCallContext context)
    {
        if (await repository.DeleteBasketAsync(request.BuyerId))
        {
            telemetry.BasketDeleted();
            logger.BasketDeleted(request.BuyerId);
        }

        return new();
    }

    private static (int Added, int Removed) CompareQuantities(CustomerBasket? previous, CustomerBasket current)
    {
        var before = Quantities(previous);
        var after = Quantities(current);

        var added = after.Sum(a => Math.Max(0, a.Value - before.GetValueOrDefault(a.Key)));
        var removed = before.Sum(b => Math.Max(0, b.Value - after.GetValueOrDefault(b.Key)));

        return (added, removed);

        static Dictionary<int, int> Quantities(CustomerBasket? basket) =>
            basket?.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity)) ?? [];
    }

    private static CustomerBasketResponse MapToCustomerBasketResponse(CustomerBasket customerBasket)
    {
        var response = new CustomerBasketResponse
        {
            BuyerId = customerBasket.BuyerId
        };
        response.Items.AddRange(customerBasket.Items.Select(MapToBasketItemResponse));

        return response;
    }

    private static BasketItemResponse MapToBasketItemResponse(BasketItem item) => new()
    {
        Id = item.Id ?? Guid.NewGuid().ToString(),
        OldUnitPrice = item.OldUnitPrice,
        ProductId = item.ProductId,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice
    };

    private static CustomerBasket MapToCustomerBasket(CustomerBasketRequest customerBasketRequest)
    {
        var response = new CustomerBasket
        {
            BuyerId = customerBasketRequest.BuyerId
        };

        foreach (var item in customerBasketRequest.Items)
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
