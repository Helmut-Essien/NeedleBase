using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using NeedleBase.Api.Configuration;

namespace NeedleBase.Api.Tests;

public class ProductionConfigValidatorTests
{
    // Shape-only values: not real hosts or credentials.
    private static Dictionary<string, string?> Complete() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=x;Username=u;Password=p",
        ["JwtSettings:SecurityKey"] = new string('k', 48),
        ["FrontendBaseUrl"] = "https://web.example.test",
        ["Platform:BaseUrl"] = "https://platform.example.test",
        ["Platform:IntegrationKey"] = "integration-key",
        ["Platform:TokenIssuer"] = "issuer"
    };

    private static ProductionConfigValidator.ValidationResult Run(Dictionary<string, string?> values) =>
        ProductionConfigValidator.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Complete_settings_are_valid()
    {
        var result = Run(Complete());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, w => w.StartsWith("Email is not configured"));
        Assert.Contains(result.Warnings, w => w.StartsWith("WhatsApp is not configured"));
    }

    [Fact]
    public void Empty_settings_list_every_critical_error()
    {
        var result = Run(new());
        Assert.Contains(result.Errors, e => e.Contains("ConnectionStrings__DefaultConnection"));
        Assert.Contains(result.Errors, e => e.Contains("JwtSettings__SecurityKey"));
        Assert.Contains(result.Errors, e => e.Contains("FrontendBaseUrl"));
        Assert.Contains(result.Errors, e => e.Contains("Platform__BaseUrl"));
        Assert.Contains(result.Errors, e => e.Contains("Platform__IntegrationKey"));
    }

    [Fact]
    public void Development_jwt_key_is_refused()
    {
        var values = Complete();
        values["JwtSettings:SecurityKey"] = "NeedleBase_Dev_Jwt_Signing_Key_Not_For_Production_0123456789";
        Assert.Contains(Run(values).Errors, e => e.Contains("Development key"));
    }

    [Theory]
    [InlineData("http://web.example.test")]
    [InlineData("web.example.test")]
    public void Frontend_url_must_be_https(string url)
    {
        var values = Complete();
        values["FrontendBaseUrl"] = url;
        Assert.Contains(Run(values).Errors, e => e.Contains("FrontendBaseUrl must be an absolute https URL"));
    }

    [Fact]
    public void Platform_not_required_when_license_key_turned_off()
    {
        var values = Complete();
        values.Remove("Platform:BaseUrl");
        values.Remove("Platform:IntegrationKey");
        values["Platform:RequireLicenseKey"] = "false";
        var result = Run(values);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains("RequireLicenseKey is false"));
    }

    [Fact]
    public void Half_configured_email_and_whatsapp_are_errors()
    {
        var values = Complete();
        values["Email:Host"] = "smtp.example.test";
        values["Email:Port"] = "99999";
        values["WhatsApp:AccessToken"] = "token";
        var errors = Run(values).Errors;
        Assert.Contains(errors, e => e.StartsWith("Email is half configured"));
        Assert.Contains(errors, e => e.StartsWith("Email__Port"));
        Assert.Contains(errors, e => e.StartsWith("WhatsApp is half configured"));
    }

    [Fact]
    public void WhatsApp_on_needs_a_template_and_warns_without_webhook()
    {
        var values = Complete();
        values["WhatsApp:AccessToken"] = "token";
        values["WhatsApp:PhoneNumberId"] = "123";
        var result = Run(values);
        Assert.Contains(result.Errors, e => e.Contains("WhatsApp__Templates"));
        values["WhatsApp:Templates:Ready"] = "order_ready";
        result = Run(values);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, w => w.StartsWith("WhatsApp webhook is off"));
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies", "10.0.0.1, not-an-ip", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownNetworks", "10.0.0.0/33", "KnownNetworks")]
    [InlineData("ForwardedHeaders:KnownNetworks", "10.0.0.0", "KnownNetworks")]
    [InlineData("DataProtection:KeysPath", "relative/keys", "DataProtection__KeysPath")]
    [InlineData("ForwardedHeaders:KnownProxies", "1", "KnownProxies")]            // IPAddress.TryParse: 0.0.0.1
    [InlineData("ForwardedHeaders:KnownProxies", "10.1", "KnownProxies")]         // 10.0.0.1
    [InlineData("ForwardedHeaders:KnownProxies", "010.0.0.1", "KnownProxies")]    // octal: 8.0.0.1
    [InlineData("ForwardedHeaders:KnownProxies", "10.0.0.1:80", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownProxies", "[::1]", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownNetworks", "0.0.0.0/0", "trust everyone")]
    [InlineData("ForwardedHeaders:KnownNetworks", "::/0", "trust everyone")]
    [InlineData("ForwardedHeaders:KnownNetworks", "10.0.0.0/+8", "KnownNetworks")]
    [InlineData("Platform:RequireLicenseKey", "yes", "Platform__RequireLicenseKey")]
    [InlineData("Email:Port", " x ", "Email__Port")]
    public void Bad_proxy_or_path_settings_are_errors(string key, string value, string expected)
    {
        var values = Complete();
        values[key] = value;
        Assert.Contains(Run(values).Errors, e => e.Contains(expected));
    }

    [Fact]
    public void Good_proxy_settings_apply()
    {
        var values = Complete();
        values["ForwardedHeaders:KnownProxies"] = "10.0.0.1,::1";
        values["ForwardedHeaders:KnownNetworks"] = "10.0.0.0/8, fd00::/8";
        Assert.True(Run(values).IsValid);
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersConfig.Apply(options, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Equal(1, options.ForwardLimit);
        Assert.Contains(options.KnownProxies, p => p.ToString() == "10.0.0.1");
        Assert.Equal(2 + 1, options.KnownNetworks.Count); // + framework loopback 127.0.0.0/8
    }

    [Fact]
    public void ThrowIfInvalid_lists_errors_without_secret_values()
    {
        var values = Complete();
        values["JwtSettings:SecurityKey"] = "short-secret";
        var warnings = new List<string>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigValidator.ThrowIfInvalid(
                new ConfigurationBuilder().AddInMemoryCollection(values).Build(), warnings.Add));
        Assert.Contains("JwtSettings__SecurityKey", ex.Message);
        Assert.DoesNotContain("short-secret", ex.Message);
        Assert.NotEmpty(warnings);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public void Runs_outside_development_and_testing(string environment, bool expected) =>
        Assert.Equal(expected, ProductionConfigValidator.ShouldValidate(new Env { EnvironmentName = environment }));

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void Whitespace_and_case_variants_are_accepted()
    {
        var values = Complete();
        values["Platform:RequireLicenseKey"] = " TRUE ";
        values["Email:Port"] = "   ";
        values["DataProtection:KeysPath"] = "   ";
        values["ForwardedHeaders:KnownProxies"] = " , 10.0.0.1 ,";
        Assert.True(Run(values).IsValid, string.Join("; ", Run(values).Errors));
    }

    [Fact]
    public void Whitespace_only_required_values_count_as_missing()
    {
        var values = Complete();
        values["ConnectionStrings:DefaultConnection"] = "   ";
        values["Platform:IntegrationKey"] = " ";
        var errors = Run(values).Errors;
        Assert.Contains(errors, e => e.Contains("ConnectionStrings__DefaultConnection"));
        Assert.Contains(errors, e => e.Contains("Platform__IntegrationKey"));
    }

    [Fact]
    public void Proxy_shorthand_is_refused_at_apply_too()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies"] = "10.1" }).Build();
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersConfig.Apply(new ForwardedHeadersOptions(), config));
    }

    [Fact]
    public void Invalid_connection_string_is_an_error_without_the_password()
    {
        var values = Complete();
        values["ConnectionStrings:DefaultConnection"] = "Host=db;Password=pw-shape;NotAKeyword=1";
        var result = ProductionConfigValidator.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Contains(result.Errors, e => e.Contains("not a valid PostgreSQL", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, e => e.Contains("pw-shape", StringComparison.Ordinal));
    }
}
