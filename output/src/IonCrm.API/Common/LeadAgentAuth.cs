using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Common;

/// <summary>Static-key auth for the lead agents (TR + global): <c>X-Agent-Key</c> or Bearer vs LeadAgent:ApiKey, fixed-time compare.</summary>
public static class LeadAgentAuth
{
    public static IActionResult? Check(HttpRequest request, IConfiguration configuration)
    {
        var expected = configuration["LeadAgent:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return new ObjectResult(ApiResponse<object>.Fail("LeadAgent:ApiKey yapılandırılmamış.", 503)) { StatusCode = 503 };
        var provided = request.Headers["X-Agent-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            var auth = request.Headers.Authorization.FirstOrDefault();
            if (auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) provided = auth[7..].Trim();
        }
        if (string.IsNullOrWhiteSpace(provided) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
            return new UnauthorizedObjectResult(ApiResponse<object>.Fail("Geçersiz ajan anahtarı.", 401));
        return null;
    }
}
