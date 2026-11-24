using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenRestoApi.Core.Application.Utilities;
using OpenRestoApi.Infrastructure.Auth;
using OpenRestoApi.Infrastructure.Persistence;
using OpenRestoApi.Infrastructure.Printing;

namespace OpenRestoApi.Controllers;

// Owner-only internal administration surface. Tokens are returned once and never exposed via GET.
[ApiController]
[Authorize(Policy = AuthPolicies.RequireOwner)]
[NoApiKeyAccess]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/admin/printing/{restaurantId:int}")]
[RequestSizeLimit(4096)]
public sealed class PrintSettingsController(AppDbContext db) : ControllerBase
{
    public sealed record RegisterRequest(string? Printer, string? Locale, bool Replace);
    public sealed record EnableRequest(bool Enabled);
    public sealed record ConfirmRequest(bool Confirm);

    private PrintQueueStore Store()
    {
        Response.Headers.CacheControl = "no-store";
        db.Database.OpenConnection();
        return new PrintQueueStore((SqliteConnection)db.Database.GetDbConnection());
    }

    [HttpGet]
    public IActionResult Get(int restaurantId)
    {
        PrintQueueStore store = Store();
        return Ok(new { agent = store.GetAgent(restaurantId), jobs = store.GetJobs(restaurantId) });
    }

    [HttpPost("register")]
    public IActionResult Register(int restaurantId, [FromBody] RegisterRequest request)
    {
        if (!PrintQueueStore.ValidAlias(request.Printer) || !PrintQueueStore.ValidLocale(request.Locale))
        {
            return BadRequest();
        }
        try
        {
            PrintQueueStore.Registration? registration = Store().Register(restaurantId, request.Printer!, request.Locale!, request.Replace);
            return registration is null ? NotFound() : Ok(registration);
        }
        catch (InvalidOperationException)
        {
            return Conflict();
        }
    }

    [HttpPost("enabled")]
    public IActionResult Enabled(int restaurantId, [FromBody] EnableRequest request)
        => Store().SetEnabled(restaurantId, request.Enabled) ? NoContent() : NotFound();

    [HttpPost("revoke")]
    public IActionResult Revoke(int restaurantId, [FromBody] ConfirmRequest request)
        => !request.Confirm ? BadRequest() : Store().Revoke(restaurantId) ? NoContent() : NotFound();

    [HttpPost("test")]
    public IActionResult Test(int restaurantId, [FromBody] ConfirmRequest request)
        => !request.Confirm ? BadRequest() : Store().QueueTest(restaurantId) ? NoContent() : NotFound();
}
