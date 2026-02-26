using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "MasterAdmin,StoreOwner,Staff")]
public sealed class AdminOrdersController : ControllerBase
{
    private const decimal DefaultTaxRateMx = 0.16m;

    private readonly OnlineShopDbContext _db;
    public AdminOrdersController(OnlineShopDbContext db) => _db = db;

    public sealed record PagedResult<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);

    public sealed record OrderListItemDto(
        Guid OrderId,
        int Status,
        DateTime CreatedAt,
        DateTime? PaidAt,
        decimal Total,
        string Currency,
        Guid StoreId,
        string? CustomerEmail,
        string? Provider,
        string? ProviderPaymentId,
        Guid? PaymentAttemptId,
        int? PaymentStatus,
        DateTime? PaymentCreatedAt
    );

    public sealed record PaymentAttemptDto(
        Guid PaymentAttemptId,
        int Status,
        decimal Amount,
        string Currency,
        string Provider,
        string ProviderPaymentId,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public sealed record OrderItemDto(
        Guid Id,
        Guid OrderId,
        Guid ProductId,
        Guid? VariantId,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        string ProductName,
        string? VariantSku,
        string? VariantSize,
        string? VariantColor,
        string? ImageUrl,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public sealed record OrderDetailDto(
        Guid OrderId,
        int Status,
        DateTime CreatedAt,
        DateTime? PaidAt,
        decimal Subtotal,
        decimal Tax,
        decimal Total,
        string Currency,
        Guid StoreId,
        string? UserId,
        string? GuestId,
        string? CustomerEmail,
        string? Provider,
        string? ProviderPaymentId,
        string? ProviderSessionId,
        DateTime UpdatedAt,
        IReadOnlyList<PaymentAttemptDto> PaymentAttempts,
        IReadOnlyList<OrderItemDto> Items
    );

    public sealed record RecalculateTotalsRequest(bool Force = false);

    public sealed record RecalculateTotalsResponse(
        Guid OrderId,
        decimal TaxRate,
        bool Changed,
        decimal BeforeSubtotal,
        decimal BeforeTax,
        decimal BeforeTotal,
        decimal NewSubtotal,
        decimal NewTax,
        decimal NewTotal,
        string? Warning
    );

    /// <summary>
    /// Lista órdenes para backoffice.
    /// GET /api/admin/orders?status=Paid&statusInt=1&storeId=...&email=...&createdFrom=...&createdTo=...&page=1&pageSize=20
    /// Nota: si se envía statusInt, tiene prioridad sobre status.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderListItemDto>>> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] int? statusInt,
        [FromQuery] Guid? storeId,
        [FromQuery] string? email,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;
        if (pageSize > 100) pageSize = 100;

        IQueryable<Order> q = _db.Orders.AsNoTracking();

        if (statusInt is not null)
            q = q.Where(o => (int)o.Status == statusInt.Value);
        else if (status is not null)
            q = q.Where(o => o.Status == status.Value);

        if (storeId is not null && storeId.Value != Guid.Empty)
            q = q.Where(o => o.StoreId == storeId.Value);

        if (!string.IsNullOrWhiteSpace(email))
        {
            var term = email.Trim();
            q = q.Where(o => o.CustomerEmail != null && EF.Functions.Like(o.CustomerEmail, $"%{term}%"));
        }

        if (createdFrom is not null)
            q = q.Where(o => o.CreatedAt >= createdFrom.Value);

        if (createdTo is not null)
            q = q.Where(o => o.CreatedAt <= createdTo.Value);

        var total = await q.CountAsync(ct);

        var orders = await q
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.CreatedAt,
                o.PaidAt,
                o.Total,
                o.Currency,
                o.StoreId,
                o.CustomerEmail,
                o.Provider,
                o.ProviderPaymentId
            })
            .ToListAsync(ct);

        var orderIds = orders.Select(x => x.Id).ToList();

        var attempts = await _db.PaymentAttempts
            .AsNoTracking()
            .Where(p => orderIds.Contains(p.OrderId))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        var lastAttemptByOrder = new Dictionary<Guid, PaymentAttempt>();
        foreach (var a in attempts)
        {
            if (!lastAttemptByOrder.ContainsKey(a.OrderId))
                lastAttemptByOrder[a.OrderId] = a;
        }

        var items = orders.Select(o =>
        {
            lastAttemptByOrder.TryGetValue(o.Id, out var a);

            return new OrderListItemDto(
                o.Id,
                (int)o.Status,
                o.CreatedAt,
                o.PaidAt,
                o.Total,
                o.Currency,
                o.StoreId,
                o.CustomerEmail,
                o.Provider,
                o.ProviderPaymentId,
                a?.Id,
                a is null ? null : (int)a.Status,
                a?.CreatedAt
            );
        }).ToList();

        return Ok(new PagedResult<OrderListItemDto>(page, pageSize, total, items));
    }

    /// <summary>
    /// Detalle de una orden con historial de attempts + items (snapshot).
    /// GET /api/admin/orders/{orderId}
    /// </summary>
    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<OrderDetailDto>> Get(Guid orderId, CancellationToken ct)
    {
        var o = await _db.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);

        if (o is null)
            return NotFound(new { error = "Order no encontrada." });

        var attemptRows = await _db.PaymentAttempts
            .AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.Status,
                p.Amount,
                p.Currency,
                p.Provider,
                p.ProviderPaymentId,
                p.CreatedAt,
                p.UpdatedAt
            })
            .ToListAsync(ct);

        var attempts = attemptRows
            .Select(p => new PaymentAttemptDto(
                p.Id,
                (int)p.Status,
                p.Amount,
                p.Currency,
                p.Provider,
                p.ProviderPaymentId,
                p.CreatedAt,
                p.UpdatedAt
            ))
            .ToList();

        var itemRows = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new
            {
                i.Id,
                i.OrderId,
                i.ProductId,
                i.VariantId,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal,
                i.ProductName,
                i.VariantSku,
                i.VariantSize,
                i.VariantColor,
                i.ImageUrl,
                i.CreatedAt,
                i.UpdatedAt
            })
            .ToListAsync(ct);

        var orderItems = itemRows
            .Select(i => new OrderItemDto(
                i.Id,
                i.OrderId,
                i.ProductId,
                i.VariantId,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal,
                i.ProductName,
                i.VariantSku,
                i.VariantSize,
                i.VariantColor,
                i.ImageUrl,
                i.CreatedAt,
                i.UpdatedAt
            ))
            .ToList();

        return Ok(new OrderDetailDto(
            OrderId: o.Id,
            Status: (int)o.Status,
            CreatedAt: o.CreatedAt,
            PaidAt: o.PaidAt,
            Subtotal: o.Subtotal,
            Tax: o.Tax,
            Total: o.Total,
            Currency: o.Currency,
            StoreId: o.StoreId,
            UserId: o.UserId,
            GuestId: o.GuestId,
            CustomerEmail: o.CustomerEmail,
            Provider: o.Provider,
            ProviderPaymentId: o.ProviderPaymentId,
            ProviderSessionId: o.ProviderSessionId,
            UpdatedAt: o.UpdatedAt,
            PaymentAttempts: attempts,
            Items: orderItems
        ));
    }

    /// <summary>
    /// Recalcula Subtotal/Tax/Total usando OrderItems (IVA 16% por ahora).
    /// POST /api/admin/orders/{orderId}/recalculate
    ///
    /// Body opcional:
    /// { "force": true|false }
    ///
    /// Seguridad:
    /// - Si hay un PaymentAttempt Succeeded y el Total cambiaría, regresa 409 salvo Force=true.
    /// </summary>
    [HttpPost("{orderId:guid}/recalculate")]
    [Authorize(Roles = "MasterAdmin")]
    public async Task<IActionResult> Recalculate(Guid orderId, [FromBody] RecalculateTotalsRequest? req, CancellationToken ct)
    {
        var force = req?.Force ?? false;

        var strategy = _db.Database.CreateExecutionStrategy();

        RecalculateTotalsResponse? result = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null)
                return;

            var beforeSubtotal = order.Subtotal;
            var beforeTax = order.Tax;
            var beforeTotal = order.Total;

            var newSubtotal = await _db.OrderItems
                .AsNoTracking()
                .Where(i => i.OrderId == orderId)
                .Select(i => i.LineTotal)
                .DefaultIfEmpty(0m)
                .SumAsync(ct);

            var newTax = Math.Round(newSubtotal * DefaultTaxRateMx, 2, MidpointRounding.AwayFromZero);
            var newTotal = newSubtotal + newTax;

            // Si ya hay pago exitoso, no permitir cambiar el total sin force
            var hasSucceededPayment = await _db.PaymentAttempts
                .AsNoTracking()
                .AnyAsync(p => p.OrderId == orderId && p.Status == PaymentStatus.Succeeded, ct);

            if (hasSucceededPayment && !force && newTotal != beforeTotal)
            {
                result = new RecalculateTotalsResponse(
                    OrderId: order.Id,
                    TaxRate: DefaultTaxRateMx,
                    Changed: false,
                    BeforeSubtotal: beforeSubtotal,
                    BeforeTax: beforeTax,
                    BeforeTotal: beforeTotal,
                    NewSubtotal: newSubtotal,
                    NewTax: newTax,
                    NewTotal: newTotal,
                    Warning: "Order ya tiene pago Succeeded; no se actualizó porque cambiaría el Total. Usa force=true si estás seguro."
                );
                await tx.CommitAsync(ct);
                return;
            }

            var changed = (beforeSubtotal != newSubtotal) || (beforeTax != newTax) || (beforeTotal != newTotal);

            if (changed)
            {
                order.Subtotal = newSubtotal;
                order.Tax = newTax;
                order.Total = newTotal;
                order.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            result = new RecalculateTotalsResponse(
                OrderId: order.Id,
                TaxRate: DefaultTaxRateMx,
                Changed: changed,
                BeforeSubtotal: beforeSubtotal,
                BeforeTax: beforeTax,
                BeforeTotal: beforeTotal,
                NewSubtotal: newSubtotal,
                NewTax: newTax,
                NewTotal: newTotal,
                Warning: hasSucceededPayment && force && newTotal != beforeTotal
                    ? "Force=true aplicado: se actualizó Total a pesar de tener pago Succeeded. Verifica conciliación."
                    : null
            );
        });

        if (result is null)
            return NotFound(new { error = "Order no encontrada." });

        // Si trae warning por pago succeeded sin force y cambio de total -> 409
        if (result.Warning is not null && result.Warning.StartsWith("Order ya tiene pago Succeeded"))
            return Conflict(new { error = result.Warning, result });

        return Ok(result);
    }
}