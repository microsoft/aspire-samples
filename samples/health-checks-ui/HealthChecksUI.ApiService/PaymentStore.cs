using System.Text.Json;
using StackExchange.Redis;

namespace HealthChecksUI.ApiService;

/// <summary>Keeps processed payments in Redis for 24 hours, plus a capped list of the most recent ones.</summary>
public sealed class PaymentStore(IConnectionMultiplexer redis)
{
    private const string RecentPaymentsKey = "payments:recent";
    private const int RecentPaymentsLimit = 100;
    private static readonly TimeSpan s_retention = TimeSpan.FromHours(24);

    private readonly IDatabase _database = redis.GetDatabase();

    public async Task SaveAsync(Payment payment)
    {
        await _database.StringSetAsync(PaymentKey(payment.Id), JsonSerializer.Serialize(payment, JsonSerializerOptions.Web), s_retention);
        await _database.ListLeftPushAsync(RecentPaymentsKey, payment.Id);
        await _database.ListTrimAsync(RecentPaymentsKey, 0, RecentPaymentsLimit - 1);
    }

    public async Task<Payment?> GetAsync(string paymentId)
    {
        var json = await _database.StringGetAsync(PaymentKey(paymentId));
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Payment>((string)json!, JsonSerializerOptions.Web);
    }

    public async Task<IReadOnlyList<Payment>> GetRecentAsync(int limit)
    {
        var ids = await _database.ListRangeAsync(RecentPaymentsKey, 0, limit - 1);
        if (ids.Length == 0)
        {
            return [];
        }

        var values = await _database.StringGetAsync([.. ids.Select(id => (RedisKey)PaymentKey(id!))]);
        return [.. values.Where(v => !v.IsNullOrEmpty).Select(v => JsonSerializer.Deserialize<Payment>((string)v!, JsonSerializerOptions.Web)!)];
    }

    private static string PaymentKey(string paymentId) => $"payments:{paymentId}";
}
