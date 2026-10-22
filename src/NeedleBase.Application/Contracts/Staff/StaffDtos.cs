namespace NeedleBase.Application.Contracts.Staff;

/// <summary>A team member's display name (no contact details).</summary>
public sealed record StaffNameDto(string UserId, string Name, string Role)
{
    public static string Compose(string? firstName, string? surname)
    {
        var name = $"{firstName?.Trim()} {surname?.Trim()}".Trim();
        return name.Length == 0 ? "Team member" : name;
    }
}
