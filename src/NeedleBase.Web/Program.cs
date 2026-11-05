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
builder.Services.AddMemoryCache();

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

// Cookie/antiforgery key ring. Must survive redeploys (or everyone is signed out): DataProtection:KeysPath (absolute,
// outside the deploy folder is best) overrides the default App_Data/Keys. Back the folder up (docs/production-config.md).
var webKeysPath = NeedleBase.Web.Configuration.HostingSettings.KeysPath(builder.Configuration);
if (builder.Environment.IsProduction() || webKeysPath is not null)
{
    var keysDirectory = NeedleBase.Web.Configuration.HostingSettings.EnsureKeysFolder(
        webKeysPath ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys"));

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

// One site, one process (docs/deployment.md): the Api runs inside the Web under /api with its own DI container, JWT
// auth, key ring, rate limits and background services. Off in Development (the Api runs on its own there) and in
// the Web's page tests.
var hostApi = builder.Configuration.GetValue("Api:InProcess", false);
if (hostApi)
{
    builder.Services.AddSingleton<NeedleBase.Web.Hosting.EmbeddedApiHostedService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<NeedleBase.Web.Hosting.EmbeddedApiHostedService>());
}

var app = builder.Build();

// Before anything of the Web's (forwarded headers, HTTPS redirection, cookies, antiforgery, error pages): "/api/..."
// goes to the Api's own pipeline unchanged; it applies its own forwarded headers, HSTS and ProblemDetails errors.
if (hostApi)
{
    var embeddedApi = app.Services.GetRequiredService<NeedleBase.Web.Hosting.EmbeddedApiHostedService>();
    app.Use((context, next) => NeedleBase.Api.Hosting.EmbeddedApi.IsApiRequest(context.Request)
        ? embeddedApi.HandleAsync(context)
        : next(context));
}

// First: behind a TLS-terminating proxy (loopback by default) the scheme/IP must be known before HSTS,
// HTTPS redirection and cookie Secure flags run — otherwise the app sees http and redirect-loops.
// Trusted proxies: loopback (default) plus ForwardedHeaders:KnownProxies (IPs) / KnownNetworks (CIDR), comma-separated,
// the same keys as the Api (docs/production-config.md).
app.UseForwardedHeaders(NeedleBase.Web.Configuration.HostingSettings.ForwardedHeaders(app.Configuration));

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
// Content-Security-Policy + basic hardening headers on every response. Everything is served from this site
// (no CDN, no inline script/style), so 'self' is enough. Development adds ws: for browser hot-reload.
var csp = "default-src 'self'; img-src 'self' data:; script-src 'self'; style-src 'self'; font-src 'self'; " +
          "connect-src 'self'" + (app.Environment.IsDevelopment() ? " ws: wss:" : "") +
          "; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var h = context.Response.Headers;
        h.ContentSecurityPolicy = csp;
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h.XFrameOptions = "DENY";
        return Task.CompletedTask;
    });
    await next();
});

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
