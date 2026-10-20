using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>
/// The single customer-matching rule for every local search (Customers tab, Orders list,
/// New order picker). Uses the indexed v3 keys; see <see cref="CustomerSearchKeys"/>.
/// </summary>
public static class CustomerSearchFilter
{
    public const int MinAnywhereDigits = 4;

    public static IQueryable<Customer> Apply(IQueryable<Customer> customers, CustomerSearchQuery query)
    {
        if (query.IsEmpty)
        {
            return customers;
        }

        var namePattern = query.NameTerm is null ? null : "% " + query.NameTerm + "%";
        var phonePattern = query.PhoneTerm is null ? null : query.PhoneTerm + "%";
        // Non-Ghana numbers are stored with their country code ("447700900123"), so a local-format query ("07700")
        // can't match by prefix. From 4 digits we also match anywhere in the digits (still digits-only, no wildcards).
        var anywherePattern = query.PhoneTerm is { Length: >= MinAnywhereDigits } ? "%" + query.PhoneTerm + "%" : null;
        return customers.Where(c =>
            (namePattern != null && EF.Functions.Like(EF.Property<string>(c, LocalDbContext.SearchNameColumn), namePattern)) ||
            (phonePattern != null && EF.Functions.Like(EF.Property<string>(c, LocalDbContext.PhoneDigitsColumn), phonePattern)) ||
            (anywherePattern != null && EF.Functions.Like(EF.Property<string>(c, LocalDbContext.PhoneDigitsColumn), anywherePattern)));
    }

    /// <summary>Best first: name starts with the term, then alphabetical.</summary>
    public static IOrderedQueryable<Customer> Rank(IQueryable<Customer> customers, CustomerSearchQuery query)
    {
        var startsWith = query.NameTerm is null ? null : " " + query.NameTerm + "%";
        return customers
            .OrderBy(c => startsWith != null && EF.Functions.Like(EF.Property<string>(c, LocalDbContext.SearchNameColumn), startsWith) ? 0 : 1)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id);
    }
}
