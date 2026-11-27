using System.Text.Json;
using StackExchange.Redis;
using AspireShop.BasketService.Models;

namespace AspireShop.BasketService.Repositories;

public class RedisBasketRepository(IConnectionMultiplexer redis) : IBasketRepository
{
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IDatabase _database = redis.GetDatabase();

    public async Task<CustomerBasket?> GetBasketAsync(string buyerId)
    {
        var data = await _database.StringGetAsync(buyerId);

        return data.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CustomerBasket>((string)data!, s_jsonSerializerOptions);
    }

    public async Task<bool> UpdateBasketAsync(CustomerBasket basket) =>
        basket.BuyerId is { Length: > 0 } buyerId
        && await _database.StringSetAsync(buyerId, JsonSerializer.Serialize(basket, s_jsonSerializerOptions));

    public Task<bool> DeleteBasketAsync(string buyerId) => _database.KeyDeleteAsync(buyerId);
}
