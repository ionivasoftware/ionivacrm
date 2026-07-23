using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// SuperAdmin-only proxy over the Liftdesk (EMS) support-chat-logs API (docs/crm-support-chat-api.md).
/// Read-only: lists the in-app support-assistant conversation logs so the support team can find gaps
/// in the help docs. The logs are kept only 10 days on the EMS side.
///
/// GET /api/v1/support-chat-logs?projectId=&amp;search=&amp;startDate=&amp;endDate=&amp;page=&amp;pageSize=
///
/// Credentials never reach the browser: auth is the static M2M Bearer key inside
/// <see cref="ILiftdeskSupportChatClient"/>. Dates are passed through verbatim; the EMS side validates.
/// </summary>
[Route("api/v1/support-chat-logs")]
[Authorize(Policy = "SuperAdmin")]
public sealed class SupportChatLogsController : ApiControllerBase
{
    private readonly ILiftdeskSupportChatClient _chatClient;
    private readonly ILogger<SupportChatLogsController> _logger;

    /// <summary>Initialises a new instance of <see cref="SupportChatLogsController"/>.</summary>
    public SupportChatLogsController(
        ILiftdeskSupportChatClient chatClient,
        ILogger<SupportChatLogsController> logger)
    {
        _chatClient = chatClient;
        _logger     = logger;
    }

    /// <summary>Lists support-chat logs from Liftdesk, filtered and paged, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskPage<LiftdeskSupportChatLog>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? projectId,
        [FromQuery] string? search,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_chatClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

        page     = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

        try
        {
            var envelope = await _chatClient.GetLogsAsync(
                projectId, Trim(search), Trim(startDate), Trim(endDate), page, pageSize, cancellationToken);

            if (!envelope.Success)
                return BadRequest(ApiResponse<object>.Fail(envelope.Message ?? "Sohbet logları alınamadı.", 400));

            var pageResult = envelope.Data ?? new LiftdeskPage<LiftdeskSupportChatLog>(
                new List<LiftdeskSupportChatLog>(), 0, page, pageSize, 0, false, false);
            return OkResponse(pageResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liftdesk support-chat log listesi alınamadı.");
            return BadRequest(ApiResponse<object>.Fail($"Liftdesk bağlantı hatası: {ex.Message}", 400));
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
