using System.Text.Json;

namespace NeedleBase.Application.Common;

public static class MeasurementFieldsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(IReadOnlyDictionary<string, decimal?> fields) =>
        JsonSerializer.Serialize(fields, Options);

    public static IReadOnlyDictionary<string, decimal?> Deserialize(string fieldsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson))
        {
            return new Dictionary<string, decimal?>();
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, decimal?>>(fieldsJson, Options);
        return parsed ?? new Dictionary<string, decimal?>();
    }
}
