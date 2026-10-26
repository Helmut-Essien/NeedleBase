using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Email;
using NeedleBase.Infrastructure.Staff;

namespace NeedleBase.Api.Tests;

public sealed class EmailQueueTests
{
    private sealed class Monitor(EmailOptions value) : IOptionsMonitor<EmailOptions>
    {
        public EmailOptions CurrentValue => value;
        public EmailOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EmailOptions, string?> listener) => null;
    }

    private sealed class FlakyTransport : IEmailTransport
    {
        public ConcurrentQueue<(string To, DateTime At)> Sent { get; } = new();
        public int BadTries;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (message.ToAddress.StartsWith("bad", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref BadTries);
                throw new InvalidOperationException("mailbox unavailable");
            }

            Sent.Enqueue((message.ToAddress, DateTime.UtcNow));
            return Task.CompletedTask;
        }
    }

    private static EmailMessage Msg(string to) => new(to, null, "s", "t", "<p>h</p>", "test");

    [Fact]
    public async Task FailingMessage_DoesNotHoldUpTheNext_AndIsRetriedThenDropped()
    {
        var options = new Monitor(new EmailOptions { Host = "smtp.example.test", Username = "u@example.test", Password = "placeholder" });
        var queue = new EmailQueue(options);
        var transport = new FlakyTransport();
        var worker = new EmailSendingService(queue, transport, options, NullLogger<EmailSendingService>.Instance)
        {
            Delays = [TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300)]
        };
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var sw = Stopwatch.StartNew();
            Assert.True(queue.TryQueue(Msg("bad@example.test")));
            Assert.True(queue.TryQueue(Msg("good@example.test")));
            while (transport.Sent.IsEmpty && sw.ElapsedMilliseconds < 5000)
            {
                await Task.Delay(5);
            }

            // Before: the good mail waited behind every retry of the bad one (5 s + 30 s in production).
            Assert.True(sw.ElapsedMilliseconds < 250, $"good mail took {sw.ElapsedMilliseconds} ms");

            while (Volatile.Read(ref transport.BadTries) < 3 && sw.ElapsedMilliseconds < 5000)
            {
                await Task.Delay(10);
            }

            await Task.Delay(700);
            Assert.Equal(3, transport.BadTries); // first try + 2 retries, then given up
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("kofi@example.com", true)]
    [InlineData("ama.owusu+shop@mail.example.com.gh", true)]
    [InlineData("Kofi <kofi@example.com>", false)]
    [InlineData("kofi@example.com\r\nBcc: x@example.com", false)]
    [InlineData("a@b.com, c@d.com", false)]
    [InlineData("kofi @example.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("kofi@", false)]
    public void InviteEmail_MustBeOnePlainAddress(string email, bool ok) => Assert.Equal(ok, StaffService.IsPlainEmail(email));
}

public sealed partial class StaffApiTests
{
    [Fact]
    public async Task Invite_RejectsHeaderTricksInEmailOrName()
    {
        var owner = await RegisterAsync("hdr");
        await AssertStatus(HttpStatusCode.BadRequest, HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest { Email = "Kofi <kofi@hdr.test>", Role = BusinessRole.Tailor });
        await AssertStatus(HttpStatusCode.BadRequest, HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest { Email = "kofi@hdr.test", Role = BusinessRole.Tailor, Name = "Kofi\r\nBcc: x@hdr.test" });
    }

    [Fact]
    public async Task WrongResetCodes_SlowDownThatAddressOnly_NotOthersOnTheSameIp()
    {
        UseEmail();
        var a = await RegisterAsync("cgnat-a");
        var b = await RegisterAsync("cgnat-b");
        var throttled = false;
        for (var i = 0; i < 20 && !throttled; i++)
        {
            using var r = await _client.PostAsJsonAsync("/api/auth/reset-password",
                new ResetPasswordRequest { Email = a.Email, Code = "1234 5678", NewPassword = "Fresh1234" }, JsonOptions);
            throttled = r.StatusCode == HttpStatusCode.TooManyRequests;
        }

        Assert.True(throttled);
        // Same IP (shared carrier address), different person: not blocked.
        Assert.Equal(StaffErrorMessages.ResetCodeInvalid, await ResetAsync(b.Email, "1234 5678", "Fresh1234"));
        using var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = b.Email }, JsonOptions);
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
    }
}
