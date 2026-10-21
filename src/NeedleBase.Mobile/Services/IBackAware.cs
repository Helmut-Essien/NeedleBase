namespace NeedleBase.Mobile.Services;

/// <summary>
/// A page view model that has a say in Android Back. <see cref="AppShell"/> handles every Back press itself
/// (pages' own OnBackButtonPressed is not reached), so it asks the current page's view model first.
/// </summary>
public interface IBackAware
{
    /// <summary>False while a save is running: Back is swallowed so the page can't be popped mid-save.</summary>
    bool CanGoBack { get; }

    /// <summary>True when the view model handled Back itself (e.g. a wizard going back one step).</summary>
    bool TryHandleBack();
}
