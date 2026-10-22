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
builder.Services.AddTransient<ForwardClientIpHandler>();
builder.Services.AddTransient<TokenRefreshHandler>();
builder.Services.AddTransient<UnauthorizedRedirectHandler>();
builder.Services.AddScoped<INeedleBaseApi, NeedleBaseApi>();

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
    client.DefaultRequestHeaders.Add("X-NeedleBase-Client", "web"); // activity log label only
    client.Timeout = TimeSpan.FromSeconds(30);
})
    .AddHttpMessageHandler<ForwardClientIpHandler>();

builder.Services.AddHttpClient("NeedleBaseApi", client =>
{
    client.BaseAddress = apiBaseUri;
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Add("X-NeedleBase-Client", "web"); // activity log label only
    client.Timeout = TimeSpan.FromSeconds(30);
})
    .AddHttpMessageHandler<TokenRefreshHandler>()
    .AddHttpMessageHandler<UnauthorizedRedirectHandler>()
    .AddHttpMessageHandler<ForwardClientIpHandler>(); // the browser's IP for the activity log (Api trusts loopback/known proxies only)

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
    options.Conventions.AllowAnonymousToPage("/Terms");
    options.Conventions.AllowAnonymousToPage("/Privacy");
});

var app = builder.Build();

// First: behind a TLS-terminating proxy (loopback by default) the scheme/IP must be known before HSTS,
// HTTPS redirection and cookie Secure flags run — otherwise the app sees http and redirect-loops.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// The browser/web app hung up (navigated away, clicked again, closed the tab): the request's own token cancels the
// work and an OperationCanceledException unwinds. That is normal, not an error: answer 499 quietly (nobody reads it)
// instead of an error log + debugger break. Real timeouts are not caught here (the caller is still waiting).
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 499; // "client closed request"
        }
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Friendly page for antiforgery 400s, unknown pages (404) etc. — never a blank status page.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.UseHttpsRedirection();
app.UseSession();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Signed-in pages carry business data: never store them (shared PCs, Back after sign-out, proxies).
// Pages only: CSS/JS keep their long-lived cache (low-end phones, mobile data).
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>() is not null)
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
        }

        return Task.CompletedTask;
    });
    await next();
});
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();

/// <summary>Visible to WebApplicationFactory in NeedleBase.Web.Tests (page rendering tests).</summary>
public partial class Program;
