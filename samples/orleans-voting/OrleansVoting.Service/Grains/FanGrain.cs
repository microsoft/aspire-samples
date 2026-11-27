using System.Diagnostics;

namespace OrleansVoting.Grains;

/// <summary>
/// One activation per fan (the grain key is the fan ID). It throttles bursts of requests from a single fan
/// before they reach the event's inventory grain, which may live in another silo.
/// </summary>
public interface IFanGrain : IGrainWithStringKey
{
    Task<ReservationResult> Reserve(string eventId, string sectionId, int quantity);
}

public sealed class FanGrain : Grain, IFanGrain
{
    // Allow a burst of 10 requests, then one request every 0.5 s as the score decays
    private const int ThrottleThreshold = 10;
    private const double DecayPerSecond = 2;

    private readonly Stopwatch _sinceLastRequest = new();
    private double _throttleScore;

    public Task<ReservationResult> Reserve(string eventId, string sectionId, int quantity)
    {
        if (Throttle() is { } retryAfter)
        {
            return Task.FromResult(new ReservationResult(ReservationOutcome.Throttled, null, SeatsRemaining: 0, SeatsHeldByFan: 0, retryAfter));
        }

        return GrainFactory.GetGrain<IEventInventoryGrain>(eventId).Reserve(this.GetPrimaryKeyString(), sectionId, quantity);
    }

    private TimeSpan? Throttle()
    {
        var elapsedSeconds = _sinceLastRequest.IsRunning ? _sinceLastRequest.Elapsed.TotalSeconds : 0;
        _sinceLastRequest.Restart();

        _throttleScore = Math.Max(0, _throttleScore - elapsedSeconds * DecayPerSecond) + 1;

        return _throttleScore > ThrottleThreshold
            ? TimeSpan.FromSeconds(Math.Ceiling((_throttleScore - ThrottleThreshold) / DecayPerSecond))
            : null;
    }
}
