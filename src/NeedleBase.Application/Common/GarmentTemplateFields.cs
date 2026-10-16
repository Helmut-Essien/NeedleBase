using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Common;

/// <summary>Default measurement field keys per garment template (UI + validation hints).</summary>
public static class GarmentTemplateFields
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Map =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [GarmentTemplate.Shirt] =
            [
                "neck", "shoulder", "chest", "waist", "sleeve_length", "shirt_length"
            ],
            [GarmentTemplate.Trousers] =
            [
                "waist", "hip", "thigh", "knee", "ankle", "inseam", "outseam"
            ],
            [GarmentTemplate.Dress] =
            [
                "bust", "waist", "hip", "shoulder", "sleeve_length", "dress_length", "neck"
            ],
            [GarmentTemplate.Skirt] =
            [
                "waist", "hip", "skirt_length"
            ],
            [GarmentTemplate.Suit] =
            [
                "neck", "shoulder", "chest", "waist", "sleeve_length", "jacket_length",
                "trouser_waist", "hip", "inseam", "outseam"
            ],
            [GarmentTemplate.Kaftan] =
            [
                "shoulder", "chest", "sleeve_length", "kaftan_length", "neck"
            ],
            [GarmentTemplate.Smock] =
            [
                "shoulder", "chest", "sleeve_length", "smock_length", "neck"
            ],
            [GarmentTemplate.SlitAndKaba] =
            [
                "bust", "waist", "hip", "shoulder", "sleeve_length", "kaba_length", "slit_length", "neck"
            ]
        };

    public static IReadOnlyList<string> GetFields(string garmentTemplate) =>
        Map.TryGetValue(garmentTemplate, out var fields) ? fields : Array.Empty<string>();

    public static string DisplayLabel(string fieldKey) => fieldKey switch
    {
        "neck" => "Neck",
        "shoulder" => "Shoulder",
        "chest" => "Chest",
        "bust" => "Bust",
        "waist" => "Waist",
        "hip" => "Hip",
        "thigh" => "Thigh",
        "knee" => "Knee",
        "ankle" => "Ankle",
        "inseam" => "Inseam",
        "outseam" => "Outseam",
        "sleeve_length" => "Sleeve length",
        "shirt_length" => "Shirt length",
        "dress_length" => "Dress length",
        "skirt_length" => "Skirt length",
        "jacket_length" => "Jacket length",
        "trouser_waist" => "Trouser waist",
        "kaftan_length" => "Kaftan length",
        "smock_length" => "Smock length",
        "kaba_length" => "Kaba length",
        "slit_length" => "Slit length",
        _ => fieldKey.Replace('_', ' ')
    };
}
