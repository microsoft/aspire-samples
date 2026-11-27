using AspireShop.BasketService.Models;

namespace AspireShop.BasketService.Repositories;

public interface IBasketRepository
{
    Task<CustomerBasket?> GetBasketAsync(string buyerId);

    /// <summary>Saves the basket and returns the basket it replaced, or <see langword="null"/> if the buyer had none.</summary>
    Task<CustomerBasket?> UpdateBasketAsync(CustomerBasket basket);

    /// <summary>Removes the basket and returns it, or <see langword="null"/> if the buyer had none.</summary>
    Task<CustomerBasket?> TakeBasketAsync(string buyerId);

    Task<bool> DeleteBasketAsync(string buyerId);
}
