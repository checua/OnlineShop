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
[Route("api/admin/payment-attempts")]
[Authorize(Roles = "MasterAdmin,StoreOwner,Staff")]
public sealed class AdminPaymentAttemptsController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    public AdminPaymentAttemptsController(OnlineShopDbContext db) => _db = db;

    public sealed record PagedResult<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);

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

    [HttpGet]
    public async Task<ActionResult<PagedResult<PaymentAttemptListItemDto>>> List(
        [FromQuery] Guid? orderId,
        [FromQuery] string? providerPaymentId,
        [FromQuery] string? provider,
        [FromQuery] int? statusInt,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;
        if (pageSize > 100) pageSize = 100;

        IQueryable<PaymentAttempt> q = _db.PaymentAttempts.AsNoTracking();

        if (orderId is not null && orderId.Value != Guid.Empty)
            q = q.Where(p => p.OrderId == orderId.Value);

        if (!string.IsNullOrWhiteSpace(providerPaymentId))
        {
            var term = providerPaymentId.Trim();
            q = q.Where(p => p.ProviderPaymentId == term);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var prov = provider.Trim();
            q = q.Where(p => p.Provider == prov);
        }

        if (statusInt is not null)
            q = q.Where(p => (int)p.Status == statusInt.Value);

        if (createdFrom is not null)
            q = q.Where(p => p.CreatedAt >= createdFrom.Value);

        if (createdTo is not null)
            q = q.Where(p => p.CreatedAt <= createdTo.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        return Ok(new PagedResult<PaymentAttemptListItemDto>(page, pageSize, total, items));
    }

    [HttpGet("{paymentAttemptId:guid}")]
    public async Task<IActionResult> Get(Guid paymentAttemptId, CancellationToken ct)
    {
        var p = await _db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentAttemptId, ct);
        if (p is null) return NotFound(new { error = "PaymentAttempt no encontrado." });

        return Ok(new
        {
            paymentAttemptId = p.Id,
            orderId = p.OrderId,
            status = (int)p.Status,
            amount = p.Amount,
            currency = p.Currency,
            provider = p.Provider,
            providerPaymentId = p.ProviderPaymentId,
            providerSessionId = p.ProviderSessionId,
            rawJson = p.RawJson,
            createdAt = p.CreatedAt,
            updatedAt = p.UpdatedAt
        });
    }
}