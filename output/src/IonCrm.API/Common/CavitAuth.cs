using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Common;

/// <summary>
/// Static-key auth for the Cavit desktop assistant: <c>X-Cavit-Key</c> or Bearer vs <c>Cavit:ApiKey</c>
/// (Railway env <c>Cavit__ApiKey</c>), fixed-time compare. Same shape as <see cref="LeadAgentAuth"/>,
/// separate key so either can be rotated or revoked alone.
/// </summary>
public static class CavitAuth
{
    public static IActionResult? Check(HttpRequest request, IConfiguration configuration)
    {
        var expected = configuration["Cavit:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return new ObjectResult(ApiResponse<object>.Fail("Cavit:ApiKey yapılandırılmamış.", 503)) { StatusCode = 503 };
        var provided = request.Headers["X-Cavit-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            var auth = request.Headers.Authorization.FirstOrDefault();
            if (auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) provided = auth[7..].Trim();
        }
        if (string.IsNullOrWhiteSpace(provided) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
            return new UnauthorizedObjectResult(ApiResponse<object>.Fail("Geçersiz Cavit anahtarı.", 401));
        return null;
    }
}
