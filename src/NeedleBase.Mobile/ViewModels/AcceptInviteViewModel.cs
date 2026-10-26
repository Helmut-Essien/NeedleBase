using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Staff;
using NeedleBase.Mobile.Messaging;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Join a business: enter the code → see shop + role → name + password → signed in.</summary>
public partial class AcceptInviteViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly INavigationService _navigation;

    public AcceptInviteViewModel(IAuthService auth, INavigationService navigation)
    {
        _auth = auth;
        _auth.OtherAccountDataPrompt ??= AuthPrompts.OtherAccountAsync;
        _navigation = navigation;
        Title = "Join your team";
        WeakReferenceMessenger.Default.Register<ClearAuthSensitiveStateMessage>(this, static (r, _) => ((AcceptInviteViewModel)r).Reset());
    }

    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string surname = string.Empty;
    [ObservableProperty] private string password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(NeedsCode), nameof(Heading), nameof(RoleSummary))]
    private InvitePreviewDto? preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasPreview => Preview is not null;
    public bool NeedsCode => Preview is null;
    public string Heading => Preview is { } p ? $"{p.BusinessName} invited you as {p.Role}" : "Enter the code the shop owner sent you";
    public string RoleSummary => Preview is { } p ? StaffRoles.Summary(p.Role) : string.Empty;
    public string PasswordHint => PasswordRules.Hint;

    partial void OnCodeChanged(string value) => ErrorMessage = null;

    [RelayCommand]
    public async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            ErrorMessage = "Enter the invite code.";
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _auth.PreviewInviteAsync(Code.Trim());
            if (preview is null)
            {
                ErrorMessage = _auth.LastErrorMessage ?? "That invite code isn't right.";
                return;
            }

            ErrorMessage = null;
            if (preview.Name is { Length: > 0 } name && string.IsNullOrWhiteSpace(FirstName))
            {
                var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                FirstName = parts.ElementAtOrDefault(0) ?? string.Empty;
                Surname = parts.ElementAtOrDefault(1) ?? string.Empty;
            }

            Preview = preview;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Can't reach NeedleBase. Joining needs a connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task JoinAsync()
    {
        if (IsBusy || Preview is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(Surname))
        {
            ErrorMessage = "Enter your first name and surname.";
            return;
        }

        if (!PasswordRules.IsStrongEnough(Password))
        {
            ErrorMessage = PasswordRules.Hint + ".";
            return;
        }

        IsBusy = true;
        try
        {
            var user = await _auth.AcceptInviteAsync(new AcceptInviteRequest
            {
                Code = Code.Trim(),
                FirstName = FirstName.Trim(),
                Surname = Surname.Trim(),
                Password = Password
            });
            if (user is null)
            {
                ErrorMessage = _auth.LastErrorMessage ?? "Couldn't join. Try again.";
                return;
            }

            Reset();
            await _navigation.ShowMainAsync();
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Can't reach NeedleBase. Joining needs a connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UseDifferentCode()
    {
        Preview = null;
        Password = string.Empty;
        ErrorMessage = null;
    }

    public void Reset()
    {
        Code = string.Empty;
        FirstName = string.Empty;
        Surname = string.Empty;
        Password = string.Empty;
        Preview = null;
        ErrorMessage = null;
    }
}
