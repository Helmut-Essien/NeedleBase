using System.Text;

namespace NeedleBase.Web.Hosting;

/// <summary>robots.txt and sitemap.xml for the public marketing pages only.</summary>
public static class SeoEndpoints
{
    private static readonly string[] SitemapPaths =
    [
        "/",
        "/Account/Login",
        "/Account/Register",
        "/Terms",
        "/Privacy",
        "/account/delete"
    ];

    public static void Map(WebApplication app)
    {
        app.MapGet("/robots.txt", (IConfiguration config) =>
        {
            var origin = SitePublicUrl.Get(config);
            var sb = new StringBuilder();
            sb.AppendLine("User-agent: *");
            sb.AppendLine("Allow: /");
            sb.AppendLine("Allow: /Account/Login");
            sb.AppendLine("Allow: /Account/Register");
            sb.AppendLine("Allow: /Terms");
            sb.AppendLine("Allow: /Privacy");
            sb.AppendLine("Allow: /account/delete");
            sb.AppendLine("Disallow: /Dashboard");
            sb.AppendLine("Disallow: /Orders");
            sb.AppendLine("Disallow: /Customers");
            sb.AppendLine("Disallow: /Payments");
            sb.AppendLine("Disallow: /Production");
            sb.AppendLine("Disallow: /Staff");
            sb.AppendLine("Disallow: /Activity");
            sb.AppendLine("Disallow: /Settings");
            sb.AppendLine("Disallow: /Profile");
            sb.AppendLine("Disallow: /Account/Logout");
            sb.AppendLine("Disallow: /Account/ForgotPassword");
            sb.AppendLine("Disallow: /Account/ResetPassword");
            sb.AppendLine("Disallow: /Account/AcceptInvite");
            sb.AppendLine("Disallow: /api");
            sb.AppendLine("Disallow: /Error");
            if (!string.IsNullOrEmpty(origin))
            {
                sb.Append("Sitemap: ").Append(SitePublicUrl.Combine(origin, "/sitemap.xml")).AppendLine();
            }

            return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
        }).AllowAnonymous();

        app.MapGet("/sitemap.xml", (IConfiguration config) =>
        {
            var origin = SitePublicUrl.Get(config);
            // Relative locs are invalid for sitemap; without a public origin return a minimal empty urlset
            // so crawlers get 200 rather than inventing a host.
            var sb = new StringBuilder();
            sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
            if (!string.IsNullOrEmpty(origin))
            {
                foreach (var path in SitemapPaths)
                {
                    sb.Append("  <url><loc>")
                        .Append(System.Security.SecurityElement.Escape(SitePublicUrl.Combine(origin, path)))
                        .AppendLine("</loc><changefreq>weekly</changefreq></url>");
                }
            }

            sb.AppendLine("</urlset>");
            return Results.Text(sb.ToString(), "application/xml; charset=utf-8");
        }).AllowAnonymous();
    }
}
