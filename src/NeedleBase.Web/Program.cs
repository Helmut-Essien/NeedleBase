using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using NeedleBase.Web.Handlers;
using NeedleBase.Web.Hosting;
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

// One site, one process (docs/deployment.md): the Api runs inside the Web under /api. Off in Development (the Api
// runs on its own there) and in the Web's page tests. Known before the HttpClients so they can short-circuit in-process.
var hostApi = builder.Configuration.GetValue("Api:InProcess", false);

builder.Services.AddHttpClient("AuthApi", client =>
{
    client.BaseAddress = apiBaseUri;
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Add("X-NeedleBase-Client", "web"); // activity log label only
    client.Timeout = TimeSpan.FromSeconds(30);
})
    .AddHttpMessageHandler<ForwardClientIpHandler>()
    .AddInProcessApiHandler(hostApi);

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
    .AddHttpMessageHandler<ForwardClientIpHandler>() // the browser's IP for the activity log (Api trusts loopback/known proxies only)
    .AddInProcessApiHandler(hostApi);

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

builder.Services.AddResponseCompression(options =>
{
    // Static assets only. Never text/html (BREACH when antiforgery/session secrets appear in
    // HTTPS HTML) and never application/json (embedded /api and /api/mobile/update must stay
    // untouched). Safe to enable over HTTPS for these mime types.
    options.EnableForHttps = true;
    options.MimeTypes = new[]
    {
        "text/css",
        "application/javascript",
        "text/javascript",
        "image/svg+xml",
    };
});
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToFolder("/Account");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Terms");
    options.Conventions.AllowAnonymousToPage("/Privacy");
    options.Conventions.AllowAnonymousToPage("/Index"); // public marketing landing
});

if (hostApi)
{
    builder.Services.AddSingleton<NeedleBase.Web.Hosting.EmbeddedApiHostedService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<NeedleBase.Web.Hosting.EmbeddedApiHostedService>());
}

builder.Services.Configure<StaticFileOptions>(options =>
{
    var provider = new FileExtensionContentTypeProvider();
    provider.Mappings[".avif"] = "image/avif";
    options.ContentTypeProvider = provider;
});

var app = builder.Build();
// Compress only the mime types registered above; skip the embedded Api branch entirely so
// /api and /api/mobile/update never pass through the compressor (even if mime types change later).
app.UseWhen(
    ctx => !NeedleBase.Api.Hosting.EmbeddedApi.IsApiRequest(ctx.Request),
    branch => branch.UseResponseCompression());

// Before anything of the Web's (forwarded headers, HTTPS redirection, cookies, antiforgery, error pages): "/api/..."
// goes to the Api's own pipeline unchanged; it applies its own forwarded headers, HSTS and ProblemDetails errors.
if (hostApi)
{
    var embeddedApi = app.Services.GetRequiredService<NeedleBase.Web.Hosting.EmbeddedApiHostedService>();
    // Terminal branch: never falls through to the Web's cookies/antiforgery. Catch client disconnects here because
    // the Web's 499 middleware below never sees /api requests.
    app.Use(async (context, next) =>
    {
        if (!NeedleBase.Api.Hosting.EmbeddedApi.IsApiRequest(context.Request))
        {
            await next(context);
            return;
        }

        try
        {
            await embeddedApi.HandleAsync(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
    });
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
// Per-request nonce so the landing page can ship JSON-LD (and only that) under a strict script-src.
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    context.Items["CspNonce"] = nonce;
    var connect = "connect-src 'self'" + (app.Environment.IsDevelopment() ? " ws: wss:" : "");
    var csp = "default-src 'self'; img-src 'self' data:; script-src 'self' 'nonce-" + nonce + "'; style-src 'self'; font-src 'self'; " +
              connect + "; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
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
// MapStaticAssets does not honour StaticFileOptions content-type mappings for .avif
// (it would otherwise be served as application/octet-stream). Set the type before headers go out.
app.Use(async (context, next) =>
{
    if (context.Request.Path.Value is string path &&
        path.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.ContentType = "image/avif";
            return Task.CompletedTask;
        });
    }
    await next();
});

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

// Public SEO files (anonymous). Sitemap lists only marketing/legal URLs — never app data routes.
NeedleBase.Web.Hosting.SeoEndpoints.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in NeedleBase.Web.Tests (page rendering tests).</summary>
public partial class Program;
