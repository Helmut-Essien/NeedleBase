using MauiIcons.Material;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Mobile.Helpers;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>List row with status chip presentation (colour + text + icon).</summary>
public sealed class OrderListRow
{
    public OrderListRow(OrderListItemDto item)
    {
        Item = item;
        StatusDisplay = OrderStatusPresentation.DisplayName(item.Status);
        StatusBackground = OrderStatusPresentation.Background(item.Status);
        StatusForeground = OrderStatusPresentation.Foreground(item.Status);
        StatusIconImage = OrderStatusPresentation.IconImage(item.Status);
        OverdueIconImage = OrderStatusPresentation.OverdueIconImage(14);
        SecondaryLine = item.SecondaryLine;
        IsOverdue = item.IsOverdue;
    }

    public OrderListItemDto Item { get; }
    public string Id => Item.Id;
    public string CustomerName => Item.CustomerName;
    public string DisplayRef => Item.DisplayRef;
    public string StatusDisplay { get; }
    public Color StatusBackground { get; }
    public Color StatusForeground { get; }
    public ImageSource StatusIconImage { get; }
    public ImageSource OverdueIconImage { get; }
    public string SecondaryLine { get; }
    public bool IsOverdue { get; }
}
