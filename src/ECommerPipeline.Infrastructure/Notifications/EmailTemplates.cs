using System.Globalization;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Infrastructure.Notifications;

/// Renders the subject + HTML body + short in-app message for each notification
/// event. Kept deliberately simple (inline HTML) — the point is reliable delivery
/// via the outbox, not template sophistication.
public static class EmailTemplates
{
    public static (string Subject, string Html, string InApp) Build(string eventType, Order order)
    {
        var total = order.TotalAmount.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
        var statusVi = StatusVi(order.Status);

        return eventType switch
        {
            OutboxEventTypes.OrderPlaced => (
                $"Đơn hàng {order.OrderNumber} đã được tạo",
                Wrap($"Cảm ơn bạn đã đặt hàng!",
                     $"Đơn <b>{order.OrderNumber}</b> với tổng tiền <b>{total}</b> đã được ghi nhận và đang chờ xử lý."),
                $"Đơn {order.OrderNumber} đã được tạo."),

            OutboxEventTypes.PaymentSucceeded => (
                $"Thanh toán thành công cho đơn {order.OrderNumber}",
                Wrap($"Đã nhận thanh toán",
                     $"Chúng tôi đã nhận thanh toán <b>{total}</b> cho đơn <b>{order.OrderNumber}</b>. Đơn đang được chuẩn bị."),
                $"Đã thanh toán đơn {order.OrderNumber}."),

            OutboxEventTypes.OrderStatusChanged => (
                $"Đơn {order.OrderNumber}: {statusVi}",
                Wrap($"Cập nhật đơn hàng",
                     $"Đơn <b>{order.OrderNumber}</b> đã chuyển sang trạng thái <b>{statusVi}</b>."),
                $"Đơn {order.OrderNumber}: {statusVi}."),

            _ => (
                $"Cập nhật đơn {order.OrderNumber}",
                Wrap("Cập nhật đơn hàng", $"Đơn <b>{order.OrderNumber}</b> có cập nhật mới."),
                $"Đơn {order.OrderNumber} có cập nhật."),
        };
    }

    private static string StatusVi(OrderStatus s) => s switch
    {
        OrderStatus.Pending   => "Chờ xử lý",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Shipped   => "Đang giao",
        OrderStatus.Delivered => "Đã giao",
        OrderStatus.Cancelled => "Đã huỷ",
        _ => s.ToString(),
    };

    private static string Wrap(string heading, string body) =>
        $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#1f2937">
          <h2 style="color:#2563eb">{heading}</h2>
          <p style="font-size:15px;line-height:1.6">{body}</p>
          <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0"/>
          <p style="font-size:12px;color:#9ca3af">ECommerPipeline — email tự động, vui lòng không trả lời.</p>
        </div>
        """;
}
