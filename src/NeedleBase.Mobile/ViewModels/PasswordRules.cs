using NeedleBase.Application.Common;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Phone alias for the shared <see cref="PasswordPolicy"/> (same rule as the server).</summary>
public static class PasswordRules
{
    public const string Hint = PasswordPolicy.Hint;

    public static bool IsStrongEnough(string? value) => PasswordPolicy.IsStrongEnough(value);
}
