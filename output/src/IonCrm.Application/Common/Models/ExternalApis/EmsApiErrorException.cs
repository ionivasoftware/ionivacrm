namespace IonCrm.Application.Common.Models.ExternalApis;

/// <summary>
/// EMS/Liftdesk CRM API'sinin kısa JSON hata gövdesi (<c>{ success:false, message, statusCode,
/// errorCode }</c>) için tipli istisna. Handler'lar <see cref="ErrorCode"/> ile kullanıcıya doğru
/// Türkçe mesajı seçer.
///
/// Bilerek <see cref="HttpRequestException"/> türetilmedi: SaasAClient'ın retry pipeline'ı o türü
/// yeniden dener; 404/409 gibi iş reddi yeniden denenmemeli.
/// </summary>
public sealed class EmsApiErrorException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }
    /// <summary>Sunucunun gönderdiği mesaj (çoğunlukla Türkçe, ekranda gösterilebilir).</summary>
    public string ApiMessage { get; }

    public EmsApiErrorException(int statusCode, string? errorCode, string apiMessage)
        : base($"HTTP {statusCode}{(errorCode is null ? "" : $" {errorCode}")}: {apiMessage}")
    {
        StatusCode = statusCode;
        ErrorCode  = errorCode;
        ApiMessage = apiMessage;
    }
}
