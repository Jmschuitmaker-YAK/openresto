using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenRestoApi.Core.Domain;
using OpenRestoApi.Infrastructure.Persistence;

namespace OpenRestoApi.Tests.Integration;

public sealed class PrintAgentApiTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    [Fact]
    public async Task PairingAndClaim_RequireSeparateCredentials_AndTokenIsReturnedOnlyOnce()
    {
        int restaurantId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var restaurant = new Restaurant { Name = "Print integration test" };
            db.Restaurants.Add(restaurant);
            await db.SaveChangesAsync();
            restaurantId = restaurant.Id;
        }

        string path = $"/api/admin/printing/{restaurantId}";
        using HttpClient anonymous = factory.CreateClient();
        using HttpClient owner = factory.CreateAuthenticatedClient();
        var registration = new { printer = "bar", locale = "nl", replace = false };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path + "/register", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsJsonAsync("/api/print-agent/v1/claim", new { })).StatusCode);

        using HttpResponseMessage paired = await owner.PostAsJsonAsync(path + "/register", registration);
        paired.EnsureSuccessStatusCode();
        Assert.Contains("no-store", paired.Headers.CacheControl!.ToString());
        JsonElement pair = await paired.Content.ReadFromJsonAsync<JsonElement>();
        string token = pair.GetProperty("token").GetString()!;
        Assert.Equal(64, token.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path + "/register", registration)).StatusCode);

        string settings = await owner.GetStringAsync(path);
        Assert.DoesNotContain(token, settings);
        Assert.DoesNotContain("tokenHash", settings, StringComparison.OrdinalIgnoreCase);
        using HttpClient agent = factory.CreateClientWithToken(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/print-agent/v1/claim", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(path + "/test", new { confirm = true })).StatusCode);
        using HttpResponseMessage claimResponse = await agent.PostAsJsonAsync("/api/print-agent/v1/claim", new { });
        claimResponse.EnsureSuccessStatusCode();
        JsonElement claim = await claimResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("TESTBON", claim.GetProperty("text").GetString()!);
        var result = new
        {
            id = claim.GetProperty("id").GetString(),
            lease = claim.GetProperty("lease").GetString(),
            status = "sent"
        };
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/print-agent/v1/result", result)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(path + "/revoke", new { confirm = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/print-agent/v1/claim", new { })).StatusCode);
    }
}
