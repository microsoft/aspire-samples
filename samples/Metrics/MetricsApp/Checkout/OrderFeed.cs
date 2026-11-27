namespace MetricsApp.Checkout;

/// <summary>Keeps the latest checkout attempts and running totals for the console's live view.</summary>
public sealed class OrderFeed(CartStore carts, PaymentGateway payments)
{
    private const int Capacity = 8;

    private readonly Lock _lock = new();
    private readonly LinkedList<RecentCheckout> _recent = new();
    private int _ordersPlaced;
    private int _paymentsDeclined;
    private decimal _revenue;

    public void Add(RecentCheckout checkout)
    {
        lock (_lock)
        {
            _recent.AddFirst(checkout);
            if (_recent.Count > Capacity)
            {
                _recent.RemoveLast();
            }

            if (checkout.OrderId is not null)
            {
                _ordersPlaced++;
                _revenue += checkout.Total;
            }
            else
            {
                _paymentsDeclined++;
            }
        }
    }

    public CheckoutOverview GetOverview()
    {
        lock (_lock)
        {
            return new CheckoutOverview(
                _ordersPlaced,
                _revenue,
                _ordersPlaced == 0 ? 0 : Math.Round(_revenue / _ordersPlaced, 2),
                _paymentsDeclined,
                carts.Count,
                payments.CardOutageEndsAt,
                [.. _recent]);
        }
    }
}
