using System.Net;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Helpers;

/// <summary>
/// Shared guards for the three Liftdesk checklist handlers (get / update / reset): kind validation,
/// Liftdesk-only customer resolution and legible failure mapping for client exceptions. Checklists
/// exist only on the Liftdesk side, so EMS/RezervAl/manual customers are rejected here.
/// </summary>
public static class LiftdeskChecklistHelper
{
    /// <summary>Maintenance (bakım) checklist kind, as used in Liftdesk URLs.</summary>
    public const string KindMaintenance = "maintenance";

    /// <summary>Fault (arıza) checklist kind, as used in Liftdesk URLs.</summary>
    public const string KindFault = "fault";

    /// <summary>Reset-only scope covering both kinds.</summary>
    public const string KindBoth = "both";

    /// <summary>Returns true when <paramref name="kind"/> is a valid checklist kind.</summary>
    public static bool IsValidKind(string? kind, bool allowBoth = false)
        => kind is KindMaintenance or KindFault || (allowBoth && kind == KindBoth);

    /// <summary>
    /// Resolves the Liftdesk company id + credentials for <paramref name="customer"/>.
    /// Returns a Turkish error message when the customer is not Liftdesk-sourced or the project's
    /// Liftdesk connection is not configured; returns null on success.
    /// </summary>
    public static string? TryResolveLiftdesk(
        Customer customer,
        Project? project,
        out int companyId,
        out string apiKey,
        out string baseUrl)
    {
        apiKey = string.Empty;
        baseUrl = string.Empty;

        if (!SaasCustomerResolver.TryResolve(customer, project, out companyId, out var key, out var url, out var kind)
            || kind != SaasSourceKind.Liftdesk)
        {
            return "Bu müşteri Liftdesk kaynaklı değil. Checklist yönetimi yalnızca Liftdesk müşterileri için kullanılabilir.";
        }

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(url))
            return "Liftdesk API anahtarı veya adresi projede tanımlı değil. Proje ayarlarından Liftdesk bağlantısını yapılandırın.";

        apiKey = key;
        baseUrl = url;
        return null;
    }

    /// <summary>
    /// Maps a Liftdesk client exception to an operator-facing Turkish message. Callers must rethrow
    /// genuine cancellations (caller token cancelled) before reaching this.
    /// </summary>
    public static string DescribeFailure(Exception ex)
    {
        // Circuit breaker check by name — Application has no Polly reference (same as the EMS handlers).
        if (ex.GetType().Name.Contains("BrokenCircuit")
            || ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            return "Liftdesk API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.";
        }

        if (ex is OperationCanceledException)
            return "Liftdesk API zaman aşımına uğradı. Lütfen tekrar deneyin.";

        if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
            return "Liftdesk API anahtarı geçersiz veya eksik (401). Proje ayarlarındaki anahtarı kontrol edin.";

        if (ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
            return "Firma Liftdesk'te bulunamadı (404). Müşteri senkronunu kontrol edin.";

        return $"Liftdesk checklist isteği başarısız: {ex.Message}";
    }
}
