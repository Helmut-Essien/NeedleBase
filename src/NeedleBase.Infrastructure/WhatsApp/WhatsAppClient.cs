using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NeedleBase.Infrastructure.WhatsApp;

/// <summary>Result of one send: provider message id, or an error (permanent = don't retry).</summary>
public sealed record WhatsAppSendResult(bool Ok, string? ProviderMessageId, string? Error, bool Permanent)
{
    public static WhatsAppSendResult Sent(string id) => new(true, id, null, false);
    public static WhatsAppSendResult Failed(string error, bool permanent) => new(false, null, error, permanent);
}

/// <summary>Sends one approved template message. Replaced by a fake in tests (no real calls to Meta).</summary>
public interface IWhatsAppClient
{
    Task<WhatsAppSendResult> SendTemplateAsync(string toDigits, string template, string language, IReadOnlyList<string> bodyParameters,
        CancellationToken cancellationToken);
}

/// <summary>Meta Cloud API: POST {BaseUrl}/{ApiVersion}/{PhoneNumberId}/messages.</summary>
public sealed class MetaWhatsAppClient : IWhatsAppClient
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<WhatsAppOptions> _options;

    public MetaWhatsAppClient(HttpClient http, IOptionsMonitor<WhatsAppOptions> options)
    {
        _http = http;
        _options = options;
    }

    public async Task<WhatsAppSendResult> SendTemplateAsync(string toDigits, string template, string language,
        IReadOnlyList<string> bodyParameters, CancellationToken cancellationToken)
    {
        var o = _options.CurrentValue;
        if (!o.IsConfigured)
        {
            return WhatsAppSendResult.Failed("WhatsApp is not configured", permanent: false);
        }

        var url = $"{o.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(o.ApiVersion)}/{Uri.EscapeDataString(o.PhoneNumberId!)}/messages";
        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = toDigits,
            type = "template",
            template = new
            {
                name = template,
                language = new { code = language },
                components = new object[]
                {
                    new
                    {
                        type = "body",
                        parameters = bodyParameters.Select(p => new { type = "text", text = p }).ToArray()
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 5, 120)));
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Interpret(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WhatsAppSendResult.Failed("Timed out", permanent: false);
        }
        catch (HttpRequestException ex)
        {
            return WhatsAppSendResult.Failed("Network error: " + ex.Message, permanent: false);
        }
    }

    /// <summary>
    /// 2xx with messages[0].id = sent. 429 / 5xx / throttling codes = retry. Other 4xx (bad number, template not
    /// approved, token revoked) = permanent: retrying would only cost money or hurt the quality rating.
    /// </summary>
    public static WhatsAppSendResult Interpret(HttpStatusCode status, string? body)
    {
        string? id = null;
        string? error = null;
        int? code = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array &&
                        messages.GetArrayLength() > 0 && messages[0].ValueKind == JsonValueKind.Object &&
                        messages[0].TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        id = idEl.GetString();
                    }

                    if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                    {
                        if (err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var ci))
                        {
                            code = ci;
                        }

                        error = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (proxy error page): fall through to the status code.
        }

        if ((int)status is >= 200 and < 300 && !string.IsNullOrEmpty(id))
        {
            return WhatsAppSendResult.Sent(id);
        }

        var text = $"{(int)status}{(code is null ? "" : $" code {code}")}: {error ?? "no details"}";
        // 4/80007/130429/131048/131056 = rate/spam limits; 131000/131016 = Meta-side errors: worth retrying.
        var transient = status == HttpStatusCode.TooManyRequests || (int)status >= 500 ||
                        code is 4 or 80007 or 130429 or 131048 or 131056 or 131000 or 131016 ||
                        (int)status is >= 200 and < 300;
        return WhatsAppSendResult.Failed(text, permanent: !transient);
    }
}
