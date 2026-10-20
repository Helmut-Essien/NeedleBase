using System.Net.Http;
using System.Text.Json;

namespace NeedleBase.Application.Contracts.Errors;

/// <summary>Client-side view of an RFC 9457 ProblemDetails body returned by the API.</summary>
public sealed record ApiProblem(
    int Status,
    string? Code,
    string? Title,
    string? Detail,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    /// <summary>
    /// Reads ProblemDetails (<c>title</c>/<c>detail</c>/<c>errors</c>/<c>code</c>) from a failed response.
    /// Never throws for a bad or missing body; falls back to the status code only.
    /// </summary>
    public static async Task<ApiProblem> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        var status = (int)response.StatusCode;
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(status, text);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiProblem(status, null, null, null, NoErrors);
        }
    }

    public static ApiProblem Parse(int status, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ApiProblem(status, null, null, null, NoErrors);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ApiProblem(status, null, null, null, NoErrors);
            }

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errorsEl.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array)
                    {
                        errors[field.Name] = field.Value.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString()!)
                            .ToArray();
                    }
                }
            }

            return new ApiProblem(
                root.TryGetProperty("status", out var s) && s.TryGetInt32(out var parsed) ? parsed : status,
                GetString(root, "code"),
                GetString(root, "title"),
                GetString(root, "detail"),
                errors);
        }
        catch (JsonException)
        {
            return new ApiProblem(status, null, null, null, NoErrors);
        }
    }

    /// <summary>First validation message, if the API returned a ValidationProblem.</summary>
    public string? FirstValidationError =>
        Errors.Values.SelectMany(v => v).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
