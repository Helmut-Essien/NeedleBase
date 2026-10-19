namespace NeedleBase.Mobile.Views;

public partial class TermsPage : ContentPage
{
    public TermsPage()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
