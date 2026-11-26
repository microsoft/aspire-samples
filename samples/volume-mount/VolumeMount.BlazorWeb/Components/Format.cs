using System.Globalization;
using VolumeMount.Data;

namespace VolumeMount.BlazorWeb.Components;

public static class Format
{
    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.#} MB")
    };

    public static string Ago(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsed = now - value;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes} min ago",
            { TotalHours: < 24 } => $"{(int)elapsed.TotalHours} h ago",
            { TotalDays: < 2 } => "yesterday",
            _ => $"{(int)elapsed.TotalDays} days ago"
        };
    }

    public static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("MMM d, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    public static string OutcomeClass(InspectionOutcome outcome) => outcome.ToString().ToLowerInvariant();

    public static string OutcomeLabel(InspectionOutcome outcome) => outcome == InspectionOutcome.Attention ? "Needs attention" : outcome.ToString();
}
