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
    public static IQueryable<Customer> Apply(IQueryable<Customer> customers, CustomerSearchQuery query)
    {
        if (query.IsEmpty)
        {
            return customers;
        }

        var namePattern = query.NameTerm is null ? null : "% " + query.NameTerm + "%";
        var phonePattern = query.PhoneTerm is null ? null : query.PhoneTerm + "%";
        return customers.Where(c =>
            (namePattern != null && EF.Functions.Like(EF.Property<string>(c, LocalDbContext.SearchNameColumn), namePattern)) ||
            (phonePattern != null && EF.Functions.Like(EF.Property<string>(c, LocalDbContext.PhoneDigitsColumn), phonePattern)));
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
