using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenRestoApi.Infrastructure.Persistence;

namespace OpenRestoApi.Migrations;

// SQL-only outbox: the booking and the immutable print snapshot commit together.
// No EF entity is mapped to these tables. A future migration that rebuilds Bookings
// must recreate these triggers; the integration test verifies their presence.
[DbContext(typeof(AppDbContext))]
[Migration("20261124180000_AddPrintOutbox")]
public sealed class AddPrintOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE PrintAgents (
                Id TEXT PRIMARY KEY NOT NULL,
                RestaurantId INTEGER NOT NULL,
                TokenHash TEXT NOT NULL UNIQUE,
                Printer TEXT NOT NULL,
                Locale TEXT NOT NULL,
                Active INTEGER NOT NULL DEFAULT 1,
                Enabled INTEGER NOT NULL DEFAULT 0,
                LastSeen INTEGER NULL
            );
            CREATE UNIQUE INDEX IX_PrintAgents_ActiveRestaurant ON PrintAgents(RestaurantId) WHERE Active=1;
            CREATE TABLE PrintJobs (
                Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                Id TEXT NOT NULL UNIQUE,
                AgentId TEXT NOT NULL REFERENCES PrintAgents(Id),
                RestaurantId INTEGER NOT NULL,
                Kind TEXT NOT NULL CHECK (Kind IN ('created','cancelled','changed','test')),
                BookingRef TEXT NOT NULL,
                CustomerName TEXT NOT NULL,
                SpecialRequests TEXT NOT NULL DEFAULT '',
                Seats INTEGER NOT NULL,
                BookingDate TEXT NULL,
                Timezone TEXT NOT NULL,
                RestaurantName TEXT NOT NULL,
                Printer TEXT NOT NULL,
                Locale TEXT NOT NULL,
                CreatedAt INTEGER NOT NULL,
                AvailableAt INTEGER NOT NULL,
                Status TEXT NOT NULL DEFAULT 'queued' CHECK (Status IN ('queued','leased','sent','uncertain')),
                Lease TEXT NULL,
                LeaseUntil INTEGER NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                Reason TEXT NULL,
                CompletedAt INTEGER NULL,
                ReprintOf TEXT NULL
            );
            CREATE INDEX IX_PrintJobs_AgentStatus ON PrintJobs(AgentId, Status, Sequence);
            CREATE TRIGGER PrintBookingCreated AFTER INSERT ON Bookings
            BEGIN
                INSERT INTO PrintJobs (Id, AgentId, RestaurantId, Kind, BookingRef, CustomerName, SpecialRequests, Seats, BookingDate, Timezone, RestaurantName, Printer, Locale, CreatedAt, AvailableAt)
            SELECT lower(hex(randomblob(16))), a.Id, NEW.RestaurantId, 'created', substr(NEW.BookingRef,1,100), substr(coalesce(NEW.CustomerName,''),1,200), substr(coalesce(NEW.SpecialRequests,''),1,500), NEW.Seats, NEW.Date, r.Timezone, substr(r.Name,1,200), a.Printer, a.Locale, unixepoch(), unixepoch()
            FROM PrintAgents a JOIN Restaurants r ON r.Id=a.RestaurantId
            WHERE a.RestaurantId=NEW.RestaurantId AND a.Active=1 AND a.Enabled=1 AND r.IsArchived=0 AND (NEW.IsCancelled=0 AND NEW.Status='Booked');
            END;
            CREATE TRIGGER PrintBookingUpdated AFTER UPDATE ON Bookings
            BEGIN
                INSERT INTO PrintJobs (Id, AgentId, RestaurantId, Kind, BookingRef, CustomerName, SpecialRequests, Seats, BookingDate, Timezone, RestaurantName, Printer, Locale, CreatedAt, AvailableAt)
            SELECT lower(hex(randomblob(16))), a.Id, OLD.RestaurantId, 'cancelled', substr(OLD.BookingRef,1,100), substr(coalesce(OLD.CustomerName,''),1,200), substr(coalesce(OLD.SpecialRequests,''),1,500), OLD.Seats, OLD.Date, r.Timezone, substr(r.Name,1,200), a.Printer, a.Locale, unixepoch(), unixepoch()
            FROM PrintAgents a JOIN Restaurants r ON r.Id=a.RestaurantId
            WHERE a.RestaurantId=OLD.RestaurantId AND a.Active=1 AND a.Enabled=1 AND r.IsArchived=0 AND (OLD.IsCancelled=0 AND (NEW.IsCancelled=1 OR OLD.RestaurantId IS NOT NEW.RestaurantId));
                INSERT INTO PrintJobs (Id, AgentId, RestaurantId, Kind, BookingRef, CustomerName, SpecialRequests, Seats, BookingDate, Timezone, RestaurantName, Printer, Locale, CreatedAt, AvailableAt)
            SELECT lower(hex(randomblob(16))), a.Id, NEW.RestaurantId, 'created', substr(NEW.BookingRef,1,100), substr(coalesce(NEW.CustomerName,''),1,200), substr(coalesce(NEW.SpecialRequests,''),1,500), NEW.Seats, NEW.Date, r.Timezone, substr(r.Name,1,200), a.Printer, a.Locale, unixepoch(), unixepoch()
            FROM PrintAgents a JOIN Restaurants r ON r.Id=a.RestaurantId
            WHERE a.RestaurantId=NEW.RestaurantId AND a.Active=1 AND a.Enabled=1 AND r.IsArchived=0 AND (NEW.IsCancelled=0 AND (OLD.IsCancelled=1 OR OLD.RestaurantId IS NOT NEW.RestaurantId));
                INSERT INTO PrintJobs (Id, AgentId, RestaurantId, Kind, BookingRef, CustomerName, SpecialRequests, Seats, BookingDate, Timezone, RestaurantName, Printer, Locale, CreatedAt, AvailableAt)
            SELECT lower(hex(randomblob(16))), a.Id, NEW.RestaurantId, 'changed', substr(NEW.BookingRef,1,100), substr(coalesce(NEW.CustomerName,''),1,200), substr(coalesce(NEW.SpecialRequests,''),1,500), NEW.Seats, NEW.Date, r.Timezone, substr(r.Name,1,200), a.Printer, a.Locale, unixepoch(), unixepoch()
            FROM PrintAgents a JOIN Restaurants r ON r.Id=a.RestaurantId
            WHERE a.RestaurantId=NEW.RestaurantId AND a.Active=1 AND a.Enabled=1 AND r.IsArchived=0 AND (OLD.IsCancelled=0 AND NEW.IsCancelled=0 AND OLD.RestaurantId=NEW.RestaurantId AND (OLD.Date IS NOT NEW.Date OR OLD.Seats IS NOT NEW.Seats OR OLD.CustomerName IS NOT NEW.CustomerName OR OLD.SpecialRequests IS NOT NEW.SpecialRequests));
            END;
            CREATE TRIGGER PrintBookingDeleted AFTER DELETE ON Bookings
            BEGIN
                INSERT INTO PrintJobs (Id, AgentId, RestaurantId, Kind, BookingRef, CustomerName, SpecialRequests, Seats, BookingDate, Timezone, RestaurantName, Printer, Locale, CreatedAt, AvailableAt)
            SELECT lower(hex(randomblob(16))), a.Id, OLD.RestaurantId, 'cancelled', substr(OLD.BookingRef,1,100), substr(coalesce(OLD.CustomerName,''),1,200), substr(coalesce(OLD.SpecialRequests,''),1,500), OLD.Seats, OLD.Date, r.Timezone, substr(r.Name,1,200), a.Printer, a.Locale, unixepoch(), unixepoch()
            FROM PrintAgents a JOIN Restaurants r ON r.Id=a.RestaurantId
            WHERE a.RestaurantId=OLD.RestaurantId AND a.Active=1 AND a.Enabled=1 AND r.IsArchived=0 AND (OLD.IsCancelled=0 AND OLD.Status='Booked');
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS PrintBookingCreated;
            DROP TRIGGER IF EXISTS PrintBookingUpdated;
            DROP TRIGGER IF EXISTS PrintBookingDeleted;
            DROP TABLE PrintJobs;
            DROP TABLE PrintAgents;
            """);
    }
}
