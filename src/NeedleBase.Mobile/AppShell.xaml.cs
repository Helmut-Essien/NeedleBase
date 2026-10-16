using NeedleBase.Mobile.Views;

namespace NeedleBase.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(CustomerEditPage), typeof(CustomerEditPage));
        Routing.RegisterRoute(nameof(CustomerDetailPage), typeof(CustomerDetailPage));
        Routing.RegisterRoute(nameof(MeasurementEditPage), typeof(MeasurementEditPage));
    }
}
