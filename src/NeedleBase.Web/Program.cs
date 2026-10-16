using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Default culture: Ghana Cedis (GHS) for MVP formatting.
var ghanaianCulture = new CultureInfo("en-GH");
ghanaianCulture.NumberFormat.CurrencySymbol = "₵";
ghanaianCulture.NumberFormat.CurrencyDecimalDigits = 2;
CultureInfo.DefaultThreadCurrentCulture = ghanaianCulture;
CultureInfo.DefaultThreadCurrentUICulture = ghanaianCulture;

builder.Services.AddRazorPages();

// Api:BaseUrl is required — never hardcode production hosts.
// CreateBuilder already loads env vars / user secrets; do not re-read after this check.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    throw new InvalidOperationException(
        "Api:BaseUrl is not configured. " +
        "Local: set Api:BaseUrl in appsettings.Development.json or user secrets. " +
        "CI: set environment variable Api__BaseUrl (or secret API_BASE_URL).");
}

if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var parsedApiBase) ||
    (parsedApiBase.Scheme != Uri.UriSchemeHttps && parsedApiBase.Scheme != Uri.UriSchemeHttp))
{
    throw new InvalidOperationException(
        "Api:BaseUrl must be an absolute http or https URL.");
}

var apiBaseUri = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");

builder.Services.AddHttpClient("NeedleBaseApi", client =>
{
    client.BaseAddress = apiBaseUri;
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
