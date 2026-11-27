using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MetricsApp.Checkout;

/// <summary>Keeps carts in memory. A real checkout service would use a distributed cache or database.</summary>
public sealed class CartStore(CheckoutTelemetry telemetry, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<Guid, Cart> _carts = new();

    public int Count => _carts.Count;

    public Cart Create()
    {
        var cart = new Cart(Guid.NewGuid(), timeProvider.GetUtcNow());
        _carts[cart.Id] = cart;
        telemetry.CartOpened();
        return cart;
    }

    public bool TryGet(Guid cartId, [NotNullWhen(true)] out Cart? cart) => _carts.TryGetValue(cartId, out cart);

    public bool Remove(Guid cartId)
    {
        if (!_carts.TryRemove(cartId, out _))
        {
            return false;
        }

        telemetry.CartClosed();
        return true;
    }
}

public sealed class Cart(Guid id, DateTimeOffset createdAt)
{
    private readonly Lock _lock = new();
    private readonly List<(Product Product, int Quantity)> _lines = [];
    private bool _locked;

    public Guid Id { get; } = id;

    public DateTimeOffset UpdatedAt { get; private set; } = createdAt;

    /// <summary>
    /// Locks the cart while it's checked out or abandoned, so a concurrent checkout, change, or abandon is rejected
    /// instead of placing a second order or changing an order that's already being paid for.
    /// </summary>
    public bool TryLock()
    {
        lock (_lock)
        {
            if (_locked)
            {
                return false;
            }

            _locked = true;
            return true;
        }
    }

    public void Unlock()
    {
        lock (_lock)
        {
            _locked = false;
        }
    }

    /// <summary>Sets the quantity of a product and returns the change in units, or null when the cart is locked.</summary>
    public int? SetQuantity(Product product, int quantity, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_locked)
            {
                return null;
            }

            var index = _lines.FindIndex(l => l.Product.Sku == product.Sku);
            var previous = index >= 0 ? _lines[index].Quantity : 0;

            if (quantity == 0 && index >= 0)
            {
                _lines.RemoveAt(index);
            }
            else if (index >= 0)
            {
                _lines[index] = (product, quantity);
            }
            else if (quantity > 0)
            {
                _lines.Add((product, quantity));
            }

            UpdatedAt = now;
            return quantity - previous;
        }
    }

    public IReadOnlyList<CartLine> GetLines()
    {
        lock (_lock)
        {
            return [.. _lines.Select(l => new CartLine(l.Product.Sku, l.Product.Name, l.Product.Price, l.Quantity, l.Product.Price * l.Quantity))];
        }
    }

    public CartDto ToDto()
    {
        var lines = GetLines();
        return new CartDto(Id, lines, lines.Sum(l => l.Quantity), lines.Sum(l => l.LineTotal), UpdatedAt);
    }
}
