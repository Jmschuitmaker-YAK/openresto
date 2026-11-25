using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenRestoApi.Infrastructure.Persistence;
using OpenRestoApi.Infrastructure.Printing;

namespace OpenRestoApi.Controllers;

// Private versioned machine protocol, not the public CLI/OpenAPI surface.
// The scoped opaque credential is checked in the same transaction as each queue operation.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public")]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/print-agent/v1")]
[RequestSizeLimit(4096)]
public sealed class PrintAgentController(AppDbContext db) : ControllerBase
{
    public sealed record ResultRequest(string? Id, string? Lease, string? Status, string? Reason);

    private string? Token()
    {
        string value = Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..] : null;
    }

    private PrintQueueStore Store()
    {
        db.Database.OpenConnection();
        return new PrintQueueStore((SqliteConnection)db.Database.GetDbConnection());
    }

    [HttpPost("claim")]
    public IActionResult Claim()
    {
        Response.Headers.CacheControl = "no-store";
        PrintQueueStore.ClaimResult result = Store().ClaimNext(Token());
        if (!result.Authorized) return Unauthorized();
        return result.Job is null ? NoContent() : Ok(result.Job);
    }

    [HttpPost("result")]
    public IActionResult Result([FromBody] ResultRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        return Store().Complete(Token(), request.Id, request.Lease, request.Status, request.Reason) switch
        {
            PrintQueueStore.ResultCode.Accepted => NoContent(),
            PrintQueueStore.ResultCode.Unauthorized => Unauthorized(),
            PrintQueueStore.ResultCode.Conflict => Conflict(),
            _ => BadRequest(),
        };
    }
}
