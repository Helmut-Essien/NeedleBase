namespace NeedleBase.Domain.Enums;

/// <summary>Garment measurement templates (plain string constants for storage + UI).</summary>
public static class GarmentTemplate
{
    public const string Shirt = "shirt";
    public const string Trousers = "trousers";
    public const string Dress = "dress";
    public const string Skirt = "skirt";
    public const string Suit = "suit";
    public const string Kaftan = "kaftan";
    public const string Smock = "smock";
    public const string SlitAndKaba = "slit_and_kaba";

    public static readonly IReadOnlyList<string> All =
    [
        Shirt, Trousers, Dress, Skirt, Suit, Kaftan, Smock, SlitAndKaba
    ];

    public static bool IsValid(string? template) =>
        template is Shirt or Trousers or Dress or Skirt or Suit or Kaftan or Smock or SlitAndKaba;

    public static string DisplayName(string template) => template switch
    {
        Shirt => "Shirt",
        Trousers => "Trousers",
        Dress => "Dress",
        Skirt => "Skirt",
        Suit => "Suit",
        Kaftan => "Kaftan",
        Smock => "Smock",
        SlitAndKaba => "Slit & kaba",
        _ => template
    };
}
