using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenRestoApi.Core.Domain;
using OpenRestoApi.Infrastructure.Persistence;
using OpenRestoApi.Infrastructure.Printing;

namespace OpenRestoApi.Tests.Migrations;

public sealed class PrintOutboxTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly PrintQueueStore _store;

    public PrintOutboxTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _db.Restaurants.Add(new Restaurant { Name = "Test restaurant", Timezone = "Europe/Amsterdam" });
        _db.SaveChanges();
        _store = new PrintQueueStore(_connection);
    }

    private int RestaurantId => _db.Restaurants.Single().Id;

    private Booking AddBooking(string reference = "testref")
    {
        var booking = new Booking
        {
            RestaurantId = RestaurantId,
            BookingRef = reference,
            CustomerName = "Test Gast",
            Seats = 4,
            Date = new DateTime(2026, 12, 4, 17, 30, 0, DateTimeKind.Utc),
        };
        _db.Bookings.Add(booking);
        _db.SaveChanges();
        return booking;
    }

    [Fact]
    public void DisabledByDefault_AndTestReceiptWorks()
    {
        var registration = _store.Register(RestaurantId, "bar", "nl")!;
        AddBooking();
        Assert.Empty(_store.GetJobs(RestaurantId));
        Assert.True(_store.QueueTest(RestaurantId));
        var claim = _store.ClaimNext(registration.Token);
        Assert.True(claim.Authorized);
        Assert.Contains("TESTBON", claim.Job!.Text);
    }

    [Fact]
    public void ConfirmedBooking_CreatesDurableSnapshot_AndAcknowledgementIsIdempotent()
    {
        var registration = _store.Register(RestaurantId, "bar", "nl")!;
        Assert.True(_store.SetEnabled(RestaurantId, true));
        Booking booking = AddBooking();
        booking.Seats = 5;
        _db.SaveChanges();
        booking.IsCancelled = true;
        _db.SaveChanges();
        Assert.Equal(3, _store.GetJobs(RestaurantId).Count);
        var first = _store.ClaimNext(registration.Token).Job!;
        Assert.Contains("18:30", first.Text);
        Assert.Contains("Gasten: 4", first.Text);
        Assert.Null(_store.ClaimNext(registration.Token).Job);
        Assert.Equal(PrintQueueStore.ResultCode.Accepted, _store.Complete(registration.Token, first.Id, first.Lease, "sent", null));
        Assert.Equal(PrintQueueStore.ResultCode.Accepted, _store.Complete(registration.Token, first.Id, first.Lease, "sent", null));
        var next = _store.ClaimNext(registration.Token).Job!;
        Assert.Contains("GEWIJZIGD", next.Text);
        Assert.Contains("Gasten: 5", next.Text);
    }

    [Fact]
    public void RevokedAgentCannotClaim_ReplacementCannotInheritJobs()
    {
        var old = _store.Register(RestaurantId, "bar", "nl")!;
        _store.SetEnabled(RestaurantId, true);
        AddBooking();
        var replacement = _store.Register(RestaurantId, "bar", "nl")!;
        Assert.False(_store.ClaimNext(old.Token).Authorized);
        Assert.False(_store.ClaimNext(new string('a',64)).Authorized);
        Assert.Null(_store.ClaimNext(replacement.Token).Job);
        Assert.Single(_store.GetJobs(RestaurantId));
    }

    [Fact]
    public void TransactionRollbackDoesNotLeavePhantomPrintJob()
    {
        _store.Register(RestaurantId, "bar", "nl");
        _store.SetEnabled(RestaurantId, true);
        using (var transaction = _db.Database.BeginTransaction())
        {
            AddBooking();
            transaction.Rollback();
        }
        Assert.Empty(_store.GetJobs(RestaurantId));
    }

    [Fact]
    public void Reprint_UsesCurrentReservationAndNewJobIdentity()
    {
        var registration = _store.Register(RestaurantId, "bar", "nl")!;
        _store.SetEnabled(RestaurantId, true);
        Booking booking = AddBooking();
        var original = _store.ClaimNext(registration.Token).Job!;
        Assert.Equal(PrintQueueStore.ResultCode.Accepted, _store.Complete(registration.Token, original.Id, original.Lease, "uncertain", null));
        _store.SetEnabled(RestaurantId, false);
        booking.Seats = 6;
        _db.SaveChanges();
        Assert.False(_store.QueueReprint(RestaurantId + 1, original.Id));
        Assert.True(_store.QueueReprint(RestaurantId, original.Id));
        Assert.False(_store.QueueReprint(RestaurantId, original.Id));
        var reprint = _store.ClaimNext(registration.Token).Job!;
        Assert.NotEqual(original.Id, reprint.Id);
        Assert.Contains("HERDRUK", reprint.Text);
        Assert.Contains("Gasten: 6", reprint.Text);
    }

    [Fact]
    public void RetryPreservesEventOrder()
    {
        var registration = _store.Register(RestaurantId, "bar", "nl")!;
        _store.SetEnabled(RestaurantId, true);
        Booking booking = AddBooking();
        booking.IsCancelled = true;
        _db.SaveChanges();
        var first = _store.ClaimNext(registration.Token).Job!;
        Assert.Equal(PrintQueueStore.ResultCode.Accepted, _store.Complete(registration.Token, first.Id, first.Lease, "retry", "printer_unreachable"));
        Assert.Null(_store.ClaimNext(registration.Token).Job);
    }

    [Fact]
    public void Retention_ExpiresOfflineJobsWithoutAnAgentPoll()
    {
        _store.Register(RestaurantId, "bar", "nl");
        _store.SetEnabled(RestaurantId, true);
        AddBooking();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE PrintJobs SET CreatedAt=unixepoch()-8*86400";
            cmd.ExecuteNonQuery();
        }
        _store.Prune();
        var job = Assert.Single(_store.GetJobs(RestaurantId));
        Assert.Equal("uncertain", job.Status);
        Assert.Equal("expired", job.Reason);
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT CustomerName FROM PrintJobs";
            Assert.Equal("", cmd.ExecuteScalar());
            cmd.CommandText = "UPDATE PrintJobs SET CompletedAt=unixepoch()-31*86400";
            cmd.ExecuteNonQuery();
        }
        _store.Prune();
        Assert.Empty(_store.GetJobs(RestaurantId));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
