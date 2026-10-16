namespace NeedleBase.Mobile.ViewModels;

public partial class HomeViewModel : BaseViewModel
{
    public HomeViewModel()
    {
        Title = "NeedleBase";
        Tagline = "You're signed in. Customers and orders land in the next slices.";
    }

    public string Tagline { get; }
}
