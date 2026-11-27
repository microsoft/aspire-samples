namespace ImageGallery.FrontEnd.Claims;

public enum ClaimLine
{
    Auto,
    Property
}

public sealed record Claim(
    string Number,
    ClaimLine Line,
    string LossType,
    string Subject,
    string Policyholder,
    string Location,
    DateOnly LossDate);

// The open claims adjusters can attach photos to. A real intake app would read these from the claims system.
public static class ClaimDirectory
{
    public static IReadOnlyList<Claim> OpenClaims { get; } =
    [
        new("CLM-26-10482", ClaimLine.Auto, "Rear-end collision", "2021 Subaru Outback", "Maya Patel", "Portland, OR", new(2026, 11, 18)),
        new("CLM-26-10517", ClaimLine.Property, "Kitchen water leak", "14 Alder Court", "Daniel Okafor", "Beaverton, OR", new(2026, 11, 20)),
        new("CLM-26-10533", ClaimLine.Property, "Hail damage", "Roof and skylight", "Rosa Jiménez", "Bend, OR", new(2026, 11, 22))
    ];

    public static Claim? Find(string claimNumber) =>
        OpenClaims.FirstOrDefault(c => string.Equals(c.Number, claimNumber, StringComparison.OrdinalIgnoreCase));
}
