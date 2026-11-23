namespace FerdiumApp.Models;

public sealed record ServiceCatalogItem(
    string Id,
    string Name,
    string Url,
    string Category,
    string AccentColor,
    string Monogram,
    string Description
);
