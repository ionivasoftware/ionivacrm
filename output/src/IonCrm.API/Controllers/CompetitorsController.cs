using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.API.Controllers;

/// <summary>
/// Rakip firmalar ("farklı firma ile çalışıyor" seçildiğinde listeden seçilen / yoksa yeni açılan).
/// Tenant kapsamı DbContext filtresinden; oturum açmış her kullanıcı kendi projelerinde okur/ekler.
/// </summary>
[Route("api/v1/competitors")]
[Authorize]
public sealed class CompetitorsController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CompetitorsController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    /// <summary>GET /api/v1/competitors?projectId=&amp;search= — ada göre sıralı; kullanım sayısı (kaç müşteri bu firmayla çalışıyor) ile.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? projectId, [FromQuery] string? search, CancellationToken cancellationToken = default)
    {
        if (projectId is { } pid && !CanAccess(pid)) return ForbiddenResponse<object>("Bu projeye erişim yok.");
        var q = _db.Competitors.AsNoTracking().AsQueryable();
        if (projectId is { } p) q = q.Where(c => c.ProjectId == p);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim().ToLowerInvariant(); q = q.Where(c => c.Name.ToLower().Contains(s)); }
        var rows = await q.OrderBy(c => c.Name).Take(500).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var usage = await _db.Customers.Where(c => c.CompetitorId != null && ids.Contains(c.CompetitorId!.Value))
            .GroupBy(c => c.CompetitorId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        return OkResponse(rows.Select(r => ToDto(r, usage.GetValueOrDefault(r.Id))).ToList());
    }

    /// <summary>POST /api/v1/competitors — {projectId, name, website?, notes?}. Aynı projede aynı ad (büyük/küçük harf duyarsız) varsa onu döndürür (201 yerine 200).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CompetitorBody body, CancellationToken cancellationToken = default)
    {
        var name = NormalizeName(body.Name);
        if (name is null) return BadRequest(ApiResponse<object>.Fail("Firma adı 2–200 karakter olmalı.", 400));
        if (body.ProjectId == Guid.Empty) return BadRequest(ApiResponse<object>.Fail("projectId gerekli.", 400));
        if (!CanAccess(body.ProjectId)) return ForbiddenResponse<object>("Bu projeye erişim yok.");

        var lower = name.ToLowerInvariant();
        var existing = await _db.Competitors.FirstOrDefaultAsync(c => c.ProjectId == body.ProjectId && c.Name.ToLower() == lower, cancellationToken);
        if (existing is not null) return OkResponse(ToDto(existing, 0), "Bu adla kayıtlı firma zaten vardı.");

        var row = new Competitor { ProjectId = body.ProjectId, Name = name, Website = Clean(body.Website, 300), Notes = Clean(body.Notes, 2000) };
        _db.Competitors.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return StatusCode(201, ApiResponse<CompetitorDto>.Ok(ToDto(row, 0)));
    }

    /// <summary>PUT /api/v1/competitors/{id} — ad / web / not düzenle.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CompetitorBody body, CancellationToken cancellationToken = default)
    {
        var row = await _db.Competitors.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (row is null) return NotFoundResponse<object>("Rakip firma bulunamadı.");
        var name = NormalizeName(body.Name);
        if (name is null) return BadRequest(ApiResponse<object>.Fail("Firma adı 2–200 karakter olmalı.", 400));
        var lower = name.ToLowerInvariant();
        var clash = await _db.Competitors.AnyAsync(c => c.Id != id && c.ProjectId == row.ProjectId && c.Name.ToLower() == lower, cancellationToken);
        if (clash) return StatusCode(409, ApiResponse<object>.Fail("Bu adla başka bir rakip firma var.", 409));
        row.Name = name; row.Website = Clean(body.Website, 300); row.Notes = Clean(body.Notes, 2000); row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return OkResponse(ToDto(row, 0));
    }

    /// <summary>DELETE /api/v1/competitors/{id} — soft delete; müşteri ve görüşme kayıtlarındaki referanslar boşa düşer.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _db.Competitors.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (row is null) return NotFoundResponse<object>("Rakip firma bulunamadı.");
        row.IsDeleted = true; row.UpdatedAt = DateTime.UtcNow;
        await _db.Customers.Where(c => c.CompetitorId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CompetitorId, (Guid?)null), cancellationToken);
        await _db.ContactHistories.IgnoreQueryFilters().Where(h => h.CompetitorId == id).ExecuteUpdateAsync(s => s.SetProperty(h => h.CompetitorId, (Guid?)null), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return OkResponse(new { deleted = true });
    }

    private bool CanAccess(Guid projectId) => _currentUser.IsSuperAdmin || _currentUser.ProjectIds.Contains(projectId);

    /// <summary>Trim + iç boşlukları tekle; 2–200 karakter dışı → null.</summary>
    public static string? NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var name = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length is < 2 or > 200 ? null : name;
    }

    private static string? Clean(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    private static CompetitorDto ToDto(Competitor c, int usage) => new(c.Id, c.ProjectId, c.Name, c.Website, c.Notes, usage, c.CreatedAt);
}

public record CompetitorBody(Guid ProjectId, string Name, string? Website, string? Notes);

public record CompetitorDto(Guid Id, Guid ProjectId, string Name, string? Website, string? Notes, int CustomerCount, DateTime CreatedAt);
