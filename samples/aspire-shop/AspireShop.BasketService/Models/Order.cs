namespace AspireShop.BasketService.Models;

public class Order
{
    public required string Id { get; set; }
    public string? BuyerId { get; set; }

    public List<BasketItem> Items { get; set; } = new();

    public int ItemCount => Items.Sum(i => i.Quantity);

    public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity);
}
