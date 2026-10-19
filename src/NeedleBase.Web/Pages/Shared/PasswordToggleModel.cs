namespace NeedleBase.Web.Pages.Shared;

public sealed class PasswordToggleModel
{
    public required string InputId { get; init; }

    public string ShowLabel { get; init; } = "Show password";

    public string HideLabel { get; init; } = "Hide password";
}
