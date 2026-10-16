using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

public class HomeViewModelTests
{
    [Fact]
    public void HomeViewModel_HasScaffoldTitle()
    {
        var vm = new HomeViewModel();
        Assert.Equal("NeedleBase", vm.Title);
        Assert.False(string.IsNullOrWhiteSpace(vm.Tagline));
    }
}
