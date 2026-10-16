namespace NeedleBase.Mobile.ViewModels;

public partial class HomeViewModel : BaseViewModel
{
    public HomeViewModel()
    {
        Title = "NeedleBase";
        Tagline = "You're signed in. Add customers and measurements from the Customers tab.";
    }

    public string Tagline { get; }
}
