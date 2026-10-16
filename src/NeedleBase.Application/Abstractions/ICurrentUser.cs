namespace NeedleBase.Application.Abstractions;

public interface ICurrentUser
{
    string? UserId { get; }
    string? BusinessId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}
