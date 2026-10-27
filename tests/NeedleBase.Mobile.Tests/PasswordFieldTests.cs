using System.Reflection;
using Microsoft.Maui.Controls;
using Microsoft.Maui;
using Microsoft.Maui.Dispatching;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;
using NeedleBase.Mobile.Views;

namespace NeedleBase.Mobile.Tests;

/// <summary>Every code-built password box uses the same show/hide eye as the sign-in page.</summary>
public class PasswordFieldTests
{
    static PasswordFieldTests() => DispatcherProvider.SetCurrent(new InlineDispatcherProvider());

    private sealed class InlineDispatcherProvider : IDispatcherProvider
    {
        private readonly InlineDispatcher _dispatcher = new();
        public IDispatcher? GetForCurrentThread() => _dispatcher;
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
    [Fact]
    public void PasswordField_StartsHidden_ToggleShowsAndHides_WithSpokenDescriptions()
    {
        var field = new PasswordField("Password", "Password", "Choose a password", "Pw");
        var toggle = FindAll<Button>(field).Single();

        Assert.True(field.Input.IsPassword);
        Assert.False(field.IsPasswordVisible);
        Assert.Equal("Show password", SemanticProperties.GetDescription(toggle));
        Assert.Equal("Choose a password", SemanticProperties.GetDescription(field.Input));
        Assert.Equal("PwToggle", toggle.AutomationId);
        Assert.True(toggle.WidthRequest >= 44 && toggle.HeightRequest >= 44);

        field.Toggle();
        Assert.False(field.Input.IsPassword);
        Assert.True(field.IsPasswordVisible);
        Assert.Equal("Hide password", SemanticProperties.GetDescription(toggle));

        field.Toggle();
        Assert.True(field.Input.IsPassword);
        Assert.Equal("Show password", SemanticProperties.GetDescription(toggle));
    }

    [Fact]
    public void Remask_HidesAShownPassword_AndIsHarmlessWhenAlreadyHidden()
    {
        var field = new PasswordField("Password", "Password", "Password", "Pw");
        field.Remask();
        Assert.True(field.Input.IsPassword);
        field.Toggle();
        field.Remask();
        Assert.True(field.Input.IsPassword);
        Assert.False(field.IsPasswordVisible);
    }

    [Fact]
    public void PasswordField_ClickingTheToggleButton_TogglesVisibility()
    {
        var field = new PasswordField("Password", "Password", "Password", "Pw");
        var toggle = FindAll<Button>(field).Single();
        ((IButtonController)toggle).SendClicked();
        Assert.False(field.Input.IsPassword);
    }

    [Fact]
    public void EveryPasswordEntryOnTheCodeBuiltPages_IsInsideAPasswordField()
    {
        var pages = new (ContentPage Page, int Expected)[]
        {
            (new ChangePasswordPage(new ChangePasswordViewModel(Stub<IAuthService>())), 3),
            (new ForgotPasswordPage(new ForgotPasswordViewModel(Stub<IAuthService>())), 2),
            (new AcceptInvitePage(new AcceptInviteViewModel(Stub<IAuthService>(), Stub<INavigationService>())), 1),
        };
        foreach (var (page, expected) in pages)
        {
            var fields = FindAll<PasswordField>(page);
            Assert.Equal(expected, fields.Count);
            var allPasswordEntries = FindAll<Entry>(page).Where(e => e.IsPassword).ToList();
            Assert.Equal(expected, allPasswordEntries.Count); // none outside a PasswordField
            Assert.All(fields, f => Assert.Contains(f.Input, allPasswordEntries));
        }
    }

    private static List<T> FindAll<T>(IVisualTreeElement root) where T : class
    {
        var found = new List<T>();
        void Walk(IVisualTreeElement node)
        {
            if (node is T hit) found.Add(hit);
            foreach (var child in node.GetVisualChildren()) Walk(child);
        }
        Walk(root);
        return found;
    }

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, NullProxy>();

    public class NullProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var type = targetMethod!.ReturnType;
            if (type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = type.GetGenericArguments()[0];
                var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
            }
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
