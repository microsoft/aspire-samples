namespace MetricsApp.Checkout;

/// <summary>The Contoso Outfitters product catalog. It's static because the sample is about checkout, not catalog management.</summary>
public static class Catalog
{
    public static IReadOnlyList<Product> Products { get; } =
    [
        new("ridgeline-rain-shell", "Ridgeline Rain Shell", "Apparel", 189.00m, "Waterproof, breathable 3-layer shell with pit zips and a helmet-compatible hood."),
        new("summit-45-backpack", "Summit 45L Backpack", "Packs", 149.00m, "Lightweight trekking pack with an adjustable torso and a removable lid."),
        new("trailhead-mid-boots", "Trailhead Mid Hiking Boots", "Footwear", 165.00m, "Waterproof leather boots with a grippy lugged outsole for rocky trails."),
        new("glacier-down-bag", "Glacier 15°F Down Sleeping Bag", "Sleep", 259.00m, "800-fill down mummy bag that packs down to the size of a loaf of bread."),
        new("merino-base-crew", "Merino Base Layer Crew", "Apparel", 79.00m, "Soft, odor-resistant merino wool base layer for cold mornings."),
        new("basecamp-stove", "Basecamp Backpacking Stove", "Cooking", 64.95m, "Boils a liter of water in three and a half minutes, with a piezo igniter."),
        new("lumen-400-headlamp", "Lumen 400 Headlamp", "Accessories", 44.95m, "Rechargeable 400-lumen headlamp with red night mode."),
        new("carbon-trekking-poles", "Carbon Trekking Poles", "Accessories", 119.00m, "Collapsible carbon poles with cork grips and quick locks.")
    ];

    public static Product? Find(string sku) =>
        Products.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
}
