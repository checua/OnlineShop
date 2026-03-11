// src/OnlineShop.Api/Controllers/AdminPaymentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/admin/payments")]
[Authorize(Roles = "MasterAdmin,StoreOwner,Staff")]
public sealed class AdminPaymentsController : ControllerBase
{
    private readonly OnlineShopDbContext _db;

    public AdminPaymentsController(OnlineShopDbContext db)
    {
        _db = db;
    }

    public sealed record PaymentAttemptListItemDto(
        Guid PaymentAttemptId,
        Guid OrderId,
        int Status,
        decimal Amount,
        string Currency,
        string Provider,
        string ProviderPaymentId,
        string? ProviderSessionId,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    /// <summary>
    /// Lista intentos de pago. Opcionalmente filtra por OrderId.
    /// GET /api/admin/payments/attempts?orderId=GUID
    /// </summary>
    [HttpGet("attempts")]
    public async Task<IActionResult> ListAttempts([FromQuery] Guid? orderId, CancellationToken ct)
    {
        var q = _db.PaymentAttempts.AsNoTracking();

        if (orderId is not null && orderId.Value != Guid.Empty)
            q = q.Where(p => p.OrderId == orderId.Value);

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Take(200)
            .Select(p => new PaymentAttemptListItemDto(
                p.Id,
                p.OrderId,
                (int)p.Status,
                p.Amount,
                p.Currency,
                p.Provider,
                p.ProviderPaymentId,
                p.ProviderSessionId,
                p.CreatedAt,
                p.UpdatedAt
            ))
            .ToListAsync(ct);

        return Ok(new { items, total = items.Count });
    }

    /// <summary>
    /// Obtiene un intento de pago por id.
    /// GET /api/admin/payments/attempts/{paymentAttemptId}
    /// </summary>
    [HttpGet("attempts/{paymentAttemptId:guid}")]
    public async Task<IActionResult> GetAttempt([FromRoute] Guid paymentAttemptId, CancellationToken ct)
    {
        var p = await _db.PaymentAttempts
            .AsNoTracking()
            .Where(x => x.Id == paymentAttemptId)
            .Select(x => new PaymentAttemptListItemDto(
                x.Id,
                x.OrderId,
                (int)x.Status,
                x.Amount,
                x.Currency,
                x.Provider,
                x.ProviderPaymentId,
                x.ProviderSessionId,
                x.CreatedAt,
                x.UpdatedAt
            ))
            .SingleOrDefaultAsync(ct);

        if (p is null)
            return NotFound(new { error = "PaymentAttempt no encontrado." });

        return Ok(p);
    }

    // ⚠️ Importante:
    // NO pongas aquí POST "manual/confirm" porque ya existe en ManualPaymentsController.
    // Si duplicas ruta+método, Swagger truena con 500.
}