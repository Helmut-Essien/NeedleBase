using CommunityToolkit.Mvvm.ComponentModel;

namespace NeedleBase.Mobile.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    public bool CanSubmit => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnBusyChanged();

    /// <summary>Hook for derived VMs that compose CanSubmit (e.g. CanSave).</summary>
    protected virtual void OnBusyChanged()
    {
    }
}
