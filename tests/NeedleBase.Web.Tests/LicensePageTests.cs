using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Pages.Account;
using NeedleBase.Web.Services;
using Xunit;
using LicensePage = NeedleBase.Web.Pages.Settings.LicenseModel;

namespace NeedleBase.Web.Tests;

public class LicensePageTests
{
    private static IMemoryCache Cache() => new MemoryCache(new MemoryCacheOptions());

    private static SubscriptionStatusDto Problem(bool ownerOnly = false) => new()
    {
        State = SubscriptionStates.Problem, Message = "Your NeedleBase license has expired.", OwnerOnly = ownerOnly, KeyHint = "…AB12"
    };

    [Fact]
    public async Task Owner_sees_status_without_the_key()
    {
        var api = new FakeApi { Subscription = () => ApiResult<SubscriptionStatusDto>.Ok(Problem()) };
        var page = Pages.With(new LicensePage(api, Cache()), BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.Equal("…AB12", page.Status!.KeyHint);
        Assert.True(page.Loaded);
    }

    [Theory]
    [InlineData(BusinessRole.Manager)]
    [InlineData(BusinessRole.Tailor)]
    public async Task Non_owner_never_loads_or_attaches(string role)
    {
        var api = new FakeApi();
        var page = Pages.With(new LicensePage(api, Cache()), role);
        page.LicenseKey = "KEY-1";
        await page.OnPostAsync(default);
        Assert.Empty(api.Calls);
        Assert.NotNull(page.PageError);
        Assert.False(page.CanRetry);
    }

    [Fact]
    public async Task Attach_trims_key_clears_banner_cache_and_redirects()
    {
        var cache = Cache();
        var api = new FakeApi { Attach = k => ApiResult<SubscriptionStatusDto>.Ok(SubscriptionStatusDto.Unlocked) };
        var page = Pages.With(new LicensePage(api, cache), BusinessRole.Owner);
        cache.Set(SubscriptionBannerViewComponent.Key(page.User), Problem());
        page.LicenseKey = "  KEY-1 ";
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(default));
        Assert.Contains("attach:KEY-1", api.Calls);
        Assert.False(cache.TryGetValue(SubscriptionBannerViewComponent.Key(page.User), out _));
        Assert.Equal("License key added.", page.Flash);
    }

    [Fact]
    public async Task Attach_error_shows_friendly_message_and_never_echoes_key()
    {
        var api = new FakeApi
        {
            Attach = _ => ApiResult<SubscriptionStatusDto>.Fail(new ApiProblem(400, LicenseErrors.EmailMismatch, "x",
                LicenseErrors.MessageFor(LicenseErrors.EmailMismatch), new Dictionary<string, string[]>()))
        };
        var page = Pages.With(new LicensePage(api, Cache()), BusinessRole.Owner);
        page.LicenseKey = "KEY-1";
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.Equal(LicenseErrors.MessageFor(LicenseErrors.EmailMismatch), page.ActionError);
        Assert.Null(page.LicenseKey);
    }

    [Fact]
    public async Task Empty_key_is_refused_without_calling_the_api()
    {
        var api = new FakeApi();
        var page = Pages.With(new LicensePage(api, Cache()), BusinessRole.Owner);
        page.LicenseKey = "   ";
        await page.OnPostAsync(default);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("attach:"));
        Assert.Equal(LicenseErrors.MessageFor(LicenseErrors.KeyRequired), page.ActionError);
    }

    [Fact]
    public async Task Banner_status_is_cached_and_failures_show_nothing()
    {
        var cache = Cache();
        var api = new FakeApi { Subscription = () => ApiResult<SubscriptionStatusDto>.Ok(Problem()) };
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("BusinessId", "b1")], "Cookies"));
        Assert.NotNull(await SubscriptionBannerViewComponent.GetAsync(api, cache, user, default));
        Assert.NotNull(await SubscriptionBannerViewComponent.GetAsync(api, cache, user, default));
        Assert.Single(api.Calls, c => c == "subscription");

        var offline = new FakeApi { Subscription = () => ApiResult<SubscriptionStatusDto>.Offline() };
        var other = new ClaimsPrincipal(new ClaimsIdentity([new Claim("BusinessId", "b2")], "Cookies"));
        Assert.Null(await SubscriptionBannerViewComponent.GetAsync(offline, cache, other, default));
    }

    private sealed class CaptureAuth : IApiAuthService
    {
        public RegisterBusinessRequest? Last { get; private set; }
        public string? LastErrorMessage { get; set; }
        public Task<bool> EmailEnabledAsync(CancellationToken c = default) => Task.FromResult(false);
        public Task<bool> RequestPasswordResetAsync(string email, CancellationToken c = default) => throw new NotSupportedException();
        public Task<bool> ResetPasswordAsync(ResetPasswordRequest r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AuthSessionResult?> LoginAsync(LoginRequest r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AuthSessionResult?> RegisterBusinessAsync(RegisterBusinessRequest r, CancellationToken c = default) { Last = r; return Task.FromResult<AuthSessionResult?>(null); }
        public Task<TokenResponse?> RefreshAsync(string a, string r, CancellationToken c = default) => throw new NotSupportedException();
        public Task LogoutAsync(string? r, CancellationToken c = default) => Task.CompletedTask;
        public Task<InvitePreviewDto?> PreviewInviteAsync(string code, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AuthSessionResult?> AcceptInviteAsync(AcceptInviteRequest r, CancellationToken c = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Register_sends_the_trimmed_license_key_and_shows_the_license_error()
    {
        var auth = new CaptureAuth { LastErrorMessage = LicenseErrors.MessageFor(LicenseErrors.KeyInUse) };
        var page = Pages.With(new RegisterModel(auth, NullLogger<RegisterModel>.Instance), null);
        page.Input = new RegisterModel.InputModel
        {
            Email = "ama@shop.gh", Password = "Secret#123", ConfirmPassword = "Secret#123", FirstName = "Ama", Surname = "Owusu",
            BusinessName = "Ama Stitches", LicenseKey = "  NB-KEY-1  ", AcceptTerms = true
        };
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.Equal("NB-KEY-1", auth.Last!.LicenseKey);
        Assert.Equal(LicenseErrors.MessageFor(LicenseErrors.KeyInUse), page.ErrorMessage);
    }

    [Fact]
    public void Register_error_copy_covers_license_codes()
    {
        foreach (var code in new[] { LicenseErrors.KeyInvalid, LicenseErrors.KeyInUse, LicenseErrors.EmailMismatch, LicenseErrors.CheckUnavailable })
        {
            Assert.Equal(LicenseErrors.MessageFor(code), AuthErrorMessages.ForRegister(new ApiProblem(400, code, null, null, new Dictionary<string, string[]>())));
        }
    }
}
