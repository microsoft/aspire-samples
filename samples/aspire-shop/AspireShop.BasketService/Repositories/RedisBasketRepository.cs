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

    public async Task<CustomerBasket?> GetBasketAsync(string buyerId) =>
        Deserialize(await _database.StringGetAsync(buyerId));

    // SET ... GET writes the basket and returns the value it replaced in one atomic command
    public async Task<CustomerBasket?> UpdateBasketAsync(CustomerBasket basket)
    {
        ArgumentException.ThrowIfNullOrEmpty(basket.BuyerId);

        return Deserialize(await _database.StringSetAndGetAsync(basket.BuyerId, JsonSerializer.Serialize(basket, s_jsonSerializerOptions)));
    }

    // GETDEL reads and removes the basket in one atomic command, so only one caller can take it
    public async Task<CustomerBasket?> TakeBasketAsync(string buyerId) =>
        Deserialize(await _database.StringGetDeleteAsync(buyerId));

    public Task<bool> DeleteBasketAsync(string buyerId) => _database.KeyDeleteAsync(buyerId);

    private static CustomerBasket? Deserialize(RedisValue data) =>
        data.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CustomerBasket>((string)data!, s_jsonSerializerOptions);
}
