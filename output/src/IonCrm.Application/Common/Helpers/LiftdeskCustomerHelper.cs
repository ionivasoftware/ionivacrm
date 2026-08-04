using System.Net;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Helpers;

/// <summary>
/// Shared guards for the CRM features that talk to a single Liftdesk tenant over the per-project
/// credentials (checklists, subscription plan, …): resolving the Liftdesk company id + credentials
/// and turning client exceptions into operator-facing Turkish messages.
///
/// These surfaces exist ONLY on the Liftdesk side, so EMS/RezervAl/manual customers are rejected here
/// rather than in each handler.
/// </summary>
public static class LiftdeskCustomerHelper
{
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
            return "Bu müşteri Liftdesk kaynaklı değil. Bu işlem yalnızca Liftdesk müşterileri için kullanılabilir.";
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

        return $"Liftdesk isteği başarısız: {ex.Message}";
    }
}
