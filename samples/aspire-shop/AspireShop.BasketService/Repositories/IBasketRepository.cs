using AspireShop.BasketService.Models;

namespace AspireShop.BasketService.Repositories;

public interface IBasketRepository
{
    Task<CustomerBasket?> GetBasketAsync(string buyerId);
    Task<bool> UpdateBasketAsync(CustomerBasket basket);
    Task<bool> DeleteBasketAsync(string buyerId);
}
