namespace NeedleBase.Mobile.ViewModels;

public partial class HomeViewModel : BaseViewModel
{
    public HomeViewModel()
    {
        Title = "NeedleBase";
        Tagline = "Offline-first tailoring — scaffold ready.";
    }

    public string Tagline { get; }
}
