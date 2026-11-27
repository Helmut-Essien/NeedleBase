using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;

namespace NeedleBase.Mobile.Tests;

// G15g: MAUI on Android announces a described Entry as "description, value", which spoke the typed password.
public class PasswordSemanticsG15Tests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NeedleBase.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Theory]
    [InlineData("LoginPage.xaml")]
    [InlineData("RegisterPage.xaml")]
    public void Password_entries_in_xaml_have_no_spoken_description(string page)
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "src", "NeedleBase.Mobile", "Views", page));
        var entries = Regex.Matches(xaml, @"<Entry\b[^>]*>", RegexOptions.Singleline)
            .Select(m => m.Value).Where(e => e.Contains("IsPassword=")).ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.DoesNotContain("SemanticProperties.Description", e));
    }
}
