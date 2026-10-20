using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>One row in the New order customer picker: initials, highlighted name, formatted phone.</summary>
public sealed class CustomerPickRow
{
    public CustomerPickRow(CustomerListItemDto customer, CustomerSearchQuery query)
    {
        Customer = customer;
        PhoneDisplay = CustomerSearchKeys.FormatPhone(customer.Phone);
        NameFormatted = BuildName(customer.Name, query.NameTerm);
    }

    public CustomerListItemDto Customer { get; }
    public string Initials => Customer.Initials;
    public string Name => Customer.Name;
    public string PhoneDisplay { get; }
    public FormattedString NameFormatted { get; }

    private static FormattedString BuildName(string name, string? term)
    {
        var formatted = new FormattedString();
        var index = CustomerSearchKeys.WordPrefixIndex(name, term);
        if (index < 0 || term is null)
        {
            formatted.Spans.Add(new Span { Text = name });
            return formatted;
        }

        if (index > 0)
        {
            formatted.Spans.Add(new Span { Text = name[..index] });
        }

        formatted.Spans.Add(new Span
        {
            Text = name.Substring(index, term.Length),
            FontAttributes = FontAttributes.Bold,
            TextColor = ResolveColor("PrimaryDark", Color.FromArgb("#247A2B"))
        });

        var rest = index + term.Length;
        if (rest < name.Length)
        {
            formatted.Spans.Add(new Span { Text = name[rest..] });
        }

        return formatted;
    }

    private static Color ResolveColor(string key, Color fallback) =>
        Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;
}
