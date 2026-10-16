using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using NeedleBase.Web.Handlers;
using NeedleBase.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Default culture: Ghana Cedis (GHS) for MVP formatting.
var ghanaianCulture = new CultureInfo("en-GH");
ghanaianCulture.NumberFormat.CurrencySymbol = "₵";
ghanaianCulture.NumberFormat.CurrencyDecimalDigits = 2;
CultureInfo.DefaultThreadCurrentCulture = ghanaianCulture;
CultureInfo.DefaultThreadCurrentUICulture = ghanaianCulture;

var cookieSecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "NeedleBase.Session";
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IApiAuthService, ApiAuthService>();
builder.Services.AddTransient<TokenRefreshHandler>();
builder.Services.AddTransient<UnauthorizedRedirectHandler>();

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

if (!builder.Environment.IsDevelopment() && parsedApiBase.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException(
        "Api:BaseUrl must use https outside Development (tokens must not traverse plain HTTP).");
}

var apiBaseUri = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");

builder.Services.AddHttpClient("AuthApi", client =>
{
    client.BaseAddress = apiBaseUri;
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("NeedleBaseApi", client =>
{
    client.BaseAddress = apiBaseUri;
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(30);
})
    .AddHttpMessageHandler<TokenRefreshHandler>()
    .AddHttpMessageHandler<UnauthorizedRedirectHandler>();

if (builder.Environment.IsProduction())
{
    var keysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys");
    var keysDirectory = new DirectoryInfo(keysPath);
    if (!keysDirectory.Exists)
    {
        keysDirectory.Create();
    }

    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(keysDirectory)
        .SetApplicationName("NeedleBase")
        .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
}

builder.Services.AddAuthentication(AuthCookieSignIn.CookieScheme)
    .AddCookie(AuthCookieSignIn.CookieScheme, options =>
    {
        options.Cookie.Name = "NeedleBaseAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
        options.Cookie.IsEssential = true;
    });

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "NeedleBase.AntiForgery";
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToFolder("/Account");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSession();
app.UseRouting();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
