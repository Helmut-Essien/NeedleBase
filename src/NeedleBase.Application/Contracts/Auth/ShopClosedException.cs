namespace NeedleBase.Application.Contracts.Auth;

/// <summary>Thrown when a write is attempted against a shop that the owner has already closed.</summary>
public sealed class ShopClosedException() : Exception("This shop account has been deleted.");
