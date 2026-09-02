namespace IonCrm.Application.Common.Models.ExternalApis;

// ── EMS CRM endpoint — /api/v1/crm/customers ─────────────────────────────────

/// <summary>Paginated response from EMS GET /api/v1/crm/customers.</summary>
public record EmsCrmCustomersResponse(
    List<EmsCrmCustomer> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages);

/// <summary>A single customer record from the EMS CRM customers endpoint.</summary>
public record EmsCrmCustomer(
    string Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? TaxNumber,
    string? Segment,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ExpirationDate);

// ── EMS extend expiration ─────────────────────────────────────────────────────

/// <summary>Response from POST /api/v1/crm/companies/{id}/extend-expiration.</summary>
public record EmsExtendExpirationResponse(
    int CompanyId,
    DateTime ExpirationDate,
    EmsExtendDuration Extended);

/// <summary>Duration details returned inside <see cref="EmsExtendExpirationResponse"/>.</summary>
public record EmsExtendDuration(string DurationType, int Amount);

// ── EMS add SMS ───────────────────────────────────────────────────────────────

/// <summary>Response from POST /api/v1/crm/companies/{id}/add-sms.</summary>
public record EmsAddSmsResponse(
    int CompanyId,
    int SmsCount,
    int Added);

// ── EMS set primary admin ─────────────────────────────────────────────────────

/// <summary>
/// Response from POST /api/v1/crm/companies/{id}/set-primary-admin. The source flips the firm's
/// primary admin (Liftdesk <c>User.IsOwner</c>) from the previous owner to <see cref="UserId"/> in
/// one transaction; the previous owner keeps its Admin role and is echoed back for the audit line.
/// </summary>
public record EmsSetPrimaryAdminResponse(
    int CompanyId,
    string UserId,
    string? PreviousUserId = null);

// ── EMS company users ─────────────────────────────────────────────────────────

/// <summary>A single user record returned by EMS GET /api/v1/crm/companies/{companyId}/users.</summary>
public record EmsCompanyUser(
    string UserId,
    string Name,
    string Surname,
    string Email,
    string Role,
    string LoginName,
    string Password,
    /// <summary>True when this user is the firm's primary admin (Liftdesk <c>User.IsOwner</c>). Null on
    /// older Liftdesk builds that do not yet emit the flag — the CRM then cannot pre-badge the current
    /// primary and relies on the set-primary-admin endpoint's server-side no-op for idempotency.</summary>
    bool? IsPrimaryAdmin = null,
    /// <summary>True when the user is active in the source (IsActive &amp;&amp; !IsDeleted). Null on older
    /// builds. When false the CRM greys out the "make primary" action; the Liftdesk endpoint enforces
    /// liveness authoritatively regardless.</summary>
    bool? IsActive = null);

/// <summary>Wrapper response from EMS GET /api/v1/crm/companies/{companyId}/users.</summary>
public record EmsCompanyUsersResponse(
    int CompanyId,
    List<EmsCompanyUser> Data);

// ── EMS company summary ───────────────────────────────────────────────────────

/// <summary>
/// Response from EMS GET /api/v1/crm/companies/{companyId}/summary.
/// Contains monthly activity counts and overall totals for the company.
/// </summary>
public record EmsCompanySummaryResponse(
    int CompanyId,
    EmsCompanySummaryTotals Totals,
    List<EmsCompanyMonthlyStat> Monthly,
    EmsCompanySummaryStorage? Storage = null);

/// <summary>Overall totals for the company (snapshot).</summary>
public record EmsCompanySummaryTotals(
    int CustomerCount,
    int ElevatorCount,
    int UserCount,
    /// <summary>Most recent login of any company staff (UTC). Null on older Liftdesk builds or
    /// for tenants whose users have not logged in since the field shipped.</summary>
    DateTime? LastLoginAt = null,
    /// <summary>
    /// Firmanın muhasebe modu: "CurrentAccount" (varsayılan — fatura kesmeyebilir, tahsilat işler)
    /// veya "Invoice". Cari-fatura kullanımını yorumlamak için gerekli: CurrentAccount modundaki bir
    /// firmada fatura sayısının 0 olması kullanmamak demek değildir, tahsilata bakmak gerekir.
    /// Ayar yoksa null.
    /// </summary>
    string? AccountingMode = null);

/// <summary>
/// Document-storage footprint of the tenant on the Liftdesk volume. Optional: only newer Liftdesk
/// deployments send it, so it stays null for EMS instances that predate the field.
///
/// NOTE: <see cref="QuotaBytesPerAssembly"/> is a PER-ASSEMBLY cap (200 MB each), whereas
/// <see cref="AssemblyDocumentBytes"/> is the tenant's total across all assemblies — the two must
/// never be divided into a single "percentage used".
/// </summary>
public record EmsCompanySummaryStorage(
    long AssemblyDocumentBytes,
    int AssemblyDocumentCount,
    long QuotaBytesPerAssembly);

/// <summary>Monthly activity breakdown for a single month (EMS field names preserved).</summary>
public record EmsCompanyMonthlyStat(
    int Year,
    int Month,
    int MaintenanceCount,
    int FaultCount,
    int PartChangeOfferCount,
    int RevisionOfferCount,
    int AssemblyOfferCount,
    /// <summary>Ay içinde AÇILAN iş emri sayısı. Liftdesk bunu 2026-08-31'den beri gönderiyor;
    /// alan burada eksik olduğu için değer sessizce düşüyor ve snapshot'ta 0 kalıyordu.</summary>
    int WorkOrderCount = 0,
    /// <summary>Ay içinde kesilen fatura sayısı — cari-fatura modülü kullanım sinyalinin "fatura"
    /// yarısı. Liftdesk henüz göndermiyor; gelene kadar 0.</summary>
    int InvoiceCount = 0,
    /// <summary>Ay içinde kaydedilen tahsilat sayısı — cari-fatura sinyalinin "cari" yarısı. Fatura
    /// kesmeyen (CurrentAccount modundaki) firmalar için asıl göstergedir. Liftdesk henüz
    /// göndermiyor; gelene kadar 0.</summary>
    int CollectionCount = 0);

// ── EMS recent payments ───────────────────────────────────────────────────────

/// <summary>
/// Response from EMS GET /api/v1/crm/payments/recent.
/// Returns payments with CompletionPayment=1 created within the last <see cref="WindowMinutes"/> minutes.
/// </summary>
public record EmsRecentPaymentsResponse(
    DateTime AsOf,
    int WindowMinutes,
    List<EmsPayment> Data);

/// <summary>A single payment record from the EMS recent payments endpoint.</summary>
public record EmsPayment(
    int Id,
    int CompanyId,
    // Nullable: Liftdesk payment records carry no user field and send userId=null; a non-nullable
    // int here made the WHOLE payment sync fail to deserialize ("$.data[0].userId"), so no payment
    // (subscription or SMS) ever became a draft invoice.
    int? UserId,
    string PaymentType,
    decimal Price,
    decimal SubTotal,
    decimal VatPrice,
    int InstallmentCount,
    string? ConversationId,
    bool CompletionPayment,
    string? CompletionProcess,
    int? ProductId,
    string? ProductName,
    DateTime CreatedOn);
