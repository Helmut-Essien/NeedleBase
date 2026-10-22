using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Production;

/// <summary>
/// Production board: one column per active status (New → Ready), earliest due first, assignee shown when set.
/// Columns load in parallel; each shows up to <see cref="ColumnSize"/> cards plus a link to the full list.
/// Tailors start on "Assigned to me".
/// </summary>
public class IndexModel : AppPageModel
{
    public const int ColumnSize = 30;

    public static readonly IReadOnlyList<string> Columns =
        [OrderStatus.New, OrderStatus.Cutting, OrderStatus.Sewing, OrderStatus.Fitting, OrderStatus.Ready];

    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    /// <summary>"me" = only my cards; "all" = everyone's. Default: tailors see theirs, others see all.</summary>
    [BindProperty(SupportsGet = true)] public string? Show { get; set; }

    public bool MineOnly { get; private set; }
    public IReadOnlyList<BoardColumn> Board { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        MineOnly = Show switch
        {
            "me" => true,
            "all" => false,
            _ => User.Role() == BusinessRole.Tailor
        };

        var columnTasks = Columns.Select(status => _api.ListOrdersAsync(
            new OrderQuery(1, ColumnSize, Status: status, Sort: OrderListSort.Due, AssignedTo: MineOnly ? "me" : null), cancellationToken)).ToList();
        var staffTask = _api.GetStaffNamesAsync(cancellationToken);
        await Task.WhenAll(columnTasks.Cast<Task>().Append(staffTask));

        var failed = columnTasks.Select(t => t.Result).FirstOrDefault(r => !r.IsSuccess);
        if (failed is not null)
        {
            return Fail(failed) ?? Page();
        }

        var staff = staffTask.Result;
        var names = staff.IsSuccess ? staff.Value!.ToDictionary(s => s.UserId, s => s.Name) : new Dictionary<string, string>();
        Board = Columns.Select((status, i) =>
        {
            var page = columnTasks[i].Result.Value!;
            return new BoardColumn(status, page.TotalCount, page.Data.Select(o => new BoardCard(o,
                o.AssignedToUserId is { Length: > 0 } id ? names.GetValueOrDefault(id, staff.IsSuccess ? "Former team member" : "Assigned") : null)).ToList());
        }).ToList();
        Loaded = true;
        return Page();
    }

    public sealed record BoardColumn(string Status, int Total, IReadOnlyList<BoardCard> Cards)
    {
        public string Title => OrderStatus.DisplayName(Status);
        public int Hidden => Math.Max(0, Total - Cards.Count);
    }

    public sealed record BoardCard(OrderListItemDto Order, string? AssigneeName);
}
