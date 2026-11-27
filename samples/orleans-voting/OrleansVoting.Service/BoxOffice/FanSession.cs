namespace OrleansVoting.BoxOffice;

/// <summary>
/// The fan using the box office UI. Blazor Server scopes services to the circuit, so each browser tab is its own fan.
/// </summary>
public sealed class FanSession
{
    public string FanId { get; } = $"fan-{Guid.NewGuid().ToString("N")[..6]}";
}
