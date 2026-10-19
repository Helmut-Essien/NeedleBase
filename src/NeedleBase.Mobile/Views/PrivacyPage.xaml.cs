namespace NeedleBase.Mobile.Views;

public partial class PrivacyPage : ContentPage
{
    public PrivacyPage()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
