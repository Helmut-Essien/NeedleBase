namespace NeedleBase.Mobile.Services;

public sealed class MauiSecureStorageService : ISecureStorageService
{
    public Task SetAsync(string key, string value) =>
        SecureStorage.Default.SetAsync(key, value);

    public Task<string?> GetAsync(string key) =>
        SecureStorage.Default.GetAsync(key);

    public void Remove(string key) =>
        SecureStorage.Default.Remove(key);
}
