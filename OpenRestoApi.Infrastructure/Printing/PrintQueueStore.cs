using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OpenRestoApi.Core.Domain;

namespace OpenRestoApi.Infrastructure.Printing;

// Internal, narrowly scoped protocol; never accepts a printer network address or raw printer commands.
public sealed partial class PrintQueueStore(SqliteConnection connection)
{
    public sealed record Registration(string AgentId, string Token);
    public sealed record Claim(string Id, string Lease, string Printer, string Text);
    public sealed record ClaimResult(bool Authorized, Claim? Job);
    public sealed record AgentStatus(string AgentId, string Printer, string Locale, bool Enabled, long? LastSeen);
    public sealed record JobStatus(string Id, string AgentId, string Kind, string Status, int Attempts, string? Reason, long CreatedAt);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();

    public static bool ValidAlias(string? alias) => alias is not null && AliasPattern().IsMatch(alias);
    public static bool ValidLocale(string? locale) => locale is "nl" or "en" or "fr" or "de" or "es";
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static string RandomId(int bytes = 16) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private SqliteCommand Command(SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandTimeout = 5;
        cmd.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return cmd;
    }

    private string? Authenticate(SqliteTransaction tx, string? token)
    {
        if (token is null || token.Length != 64 || !token.All(char.IsAsciiHexDigit))
        {
            return null;
        }
        using SqliteCommand cmd = Command(tx, """
            SELECT a.Id FROM PrintAgents a JOIN Restaurants r ON r.Id=a.RestaurantId
            WHERE a.TokenHash=$hash AND a.Active=1 AND r.IsArchived=0
            """, ("$hash", Hash(token)));
        return cmd.ExecuteScalar() as string;
    }

    // A replacement is a new identity. Existing jobs stay with the old agent; no silent replay.
    // Registration is disabled by default, so pairing does not suddenly print live reservations.
    public Registration? Register(int restaurantId, string printer, string locale)
    {
        if (!ValidAlias(printer) || !ValidLocale(locale))
        {
            throw new ArgumentException("Invalid printer alias or locale.");
        }
        using SqliteTransaction tx = connection.BeginTransaction(deferred: false);
        using (SqliteCommand exists = Command(tx, "SELECT Id FROM Restaurants WHERE Id=$id AND IsArchived=0", ("$id", restaurantId)))
        {
            if (exists.ExecuteScalar() is null)
            {
                return null;
            }
        }
        string id = RandomId();
        string token = RandomId(32);
        using (SqliteCommand deactivate = Command(tx, "UPDATE PrintAgents SET Active=0, Enabled=0 WHERE RestaurantId=$id AND Active=1", ("$id", restaurantId)))
        {
            deactivate.ExecuteNonQuery();
        }
        using (SqliteCommand create = Command(tx, """
            INSERT INTO PrintAgents(Id,RestaurantId,TokenHash,Printer,Locale) VALUES ($id,$restaurant,$hash,$printer,$locale)
            """, ("$id", id), ("$restaurant", restaurantId), ("$hash", Hash(token)), ("$printer", printer), ("$locale", locale)))
        {
            create.ExecuteNonQuery();
        }
        tx.Commit();
        return new Registration(id, token);
    }

    public bool SetEnabled(int restaurantId, bool enabled)
    {
        using SqliteTransaction tx = connection.BeginTransaction(deferred: false);
        using SqliteCommand cmd = Command(tx, "UPDATE PrintAgents SET Enabled=$enabled WHERE RestaurantId=$id AND Active=1", ("$enabled", enabled), ("$id", restaurantId));
        bool found = cmd.ExecuteNonQuery() == 1;
        tx.Commit();
        return found;
    }

    public bool Revoke(int restaurantId)
    {
        using SqliteTransaction tx = connection.BeginTransaction(deferred: false);
        using SqliteCommand cmd = Command(tx, "UPDATE PrintAgents SET Active=0, Enabled=0 WHERE RestaurantId=$id AND Active=1", ("$id", restaurantId));
        bool found = cmd.ExecuteNonQuery() == 1;
        tx.Commit();
        return found;
    }

    public bool QueueTest(int restaurantId)
    {
        using SqliteTransaction tx = connection.BeginTransaction(deferred: false);
        using SqliteCommand cmd = Command(tx, """
            INSERT INTO PrintJobs(Id,AgentId,RestaurantId,Kind,BookingRef,CustomerName,Seats,Timezone,RestaurantName,Printer,Locale,CreatedAt,AvailableAt)
            SELECT $job,a.Id,a.RestaurantId,'test','','',0,r.Timezone,r.Name,a.Printer,a.Locale,$now,$now
            FROM PrintAgents a JOIN Restaurants r ON r.Id=a.RestaurantId
            WHERE a.RestaurantId=$id AND a.Active=1 AND r.IsArchived=0
            """, ("$job", RandomId()), ("$now", Now), ("$id", restaurantId));
        bool found = cmd.ExecuteNonQuery() == 1;
        tx.Commit();
        return found;
    }

    public AgentStatus? GetAgent(int restaurantId)
    {
        using SqliteTransaction tx = connection.BeginTransaction();
        using SqliteCommand cmd = Command(tx, "SELECT Id,Printer,Locale,Enabled,LastSeen FROM PrintAgents WHERE RestaurantId=$id AND Active=1", ("$id", restaurantId));
        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? new AgentStatus(r.GetString(0),r.GetString(1),r.GetString(2),r.GetBoolean(3),r.IsDBNull(4) ? null : r.GetInt64(4)) : null;
    }

    public IReadOnlyList<JobStatus> GetJobs(int restaurantId)
    {
        using SqliteTransaction tx = connection.BeginTransaction();
        using SqliteCommand cmd = Command(tx, """
            SELECT Id,AgentId,Kind,Status,Attempts,Reason,CreatedAt FROM PrintJobs
            WHERE RestaurantId=$id ORDER BY Sequence DESC LIMIT 100
            """, ("$id", restaurantId));
        using SqliteDataReader r = cmd.ExecuteReader();
        var jobs = new List<JobStatus>();
        while (r.Read())
        {
            jobs.Add(new JobStatus(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.IsDBNull(5) ? null : r.GetString(5),r.GetInt64(6)));
        }
        return jobs;
    }

    public ClaimResult ClaimNext(string? token)
    {
        if (token is null || token.Length != 64 || !token.All(char.IsAsciiHexDigit)) return new ClaimResult(false, null);
        using SqliteTransaction tx = connection.BeginTransaction(deferred: false);
        string? agent = Authenticate(tx, token);
        if (agent is null)
        {
            return new ClaimResult(false, null);
        }
        long now = Now;
        using (SqliteCommand heartbeat = Command(tx, "UPDATE PrintAgents SET LastSeen=$now WHERE Id=$agent", ("$now", now), ("$agent", agent)))
        {
            heartbeat.ExecuteNonQuery();
        }
        string lease = RandomId(32);
        Claim? job = null;
        // An unexpired lease blocks any other job. An expired lease is reclaimed only by
        // the same agent, before any queued job. Pausing new alerts does not discard old jobs.
        using (SqliteCommand cmd = Command(tx, """
            UPDATE PrintJobs SET Status='leased', Lease=$lease, LeaseUntil=$until, Attempts=Attempts+1
            WHERE Id=(
                SELECT Id FROM PrintJobs
                WHERE AgentId=$agent AND ((Status='leased' AND LeaseUntil<=$now) OR (Status='queued' AND AvailableAt<=$now))
                ORDER BY CASE WHEN Status='leased' THEN 0 ELSE 1 END, Sequence LIMIT 1
            ) AND NOT EXISTS (SELECT 1 FROM PrintJobs WHERE AgentId=$agent AND Status='leased' AND LeaseUntil>$now)
            RETURNING Id,Printer,Kind,BookingRef,CustomerName,Seats,BookingDate,Timezone,RestaurantName,Locale,SpecialRequests
            """, ("$lease", lease), ("$until", now+120), ("$agent", agent), ("$now", now)))
        using (SqliteDataReader r = cmd.ExecuteReader())
        {
            if (r.Read())
            {
                job = new Claim(r.GetString(0), lease, r.GetString(1),
                    Receipt(r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.IsDBNull(6) ? null : r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetString(10)));
            }
        }
        tx.Commit();
        return new ClaimResult(true, job);
    }

    public enum ResultCode { Accepted, Unauthorized, Conflict, Invalid }

    public ResultCode Complete(string? token, string? id, string? lease, string? status, string? reason)
    {
        if (id is null || id.Length != 32 || !id.All(char.IsAsciiHexDigit) ||
            lease is null || lease.Length != 64 || !lease.All(char.IsAsciiHexDigit) ||
            status is not ("sent" or "retry" or "uncertain"))
        {
            return ResultCode.Invalid;
        }
        // Never accept arbitrary error strings: they could contain guest data or secrets.
        reason = reason switch
        {
            "unknown_printer" or "printer_unreachable" or "printer_deadline_failed" or "journal_write_failed" or
            "interrupted_previous_attempt" or "journal_unreadable" or "invalid_receipt" or
            "printer_write_interrupted" or "journal_commit_failed" => reason,
            _ => null,
        };
        using SqliteTransaction tx = connection.BeginTransaction(deferred: false);
        string? agent = Authenticate(tx, token);
        if (agent is null)
        {
            return ResultCode.Unauthorized;
        }
        long now = Now;
        using SqliteCommand cmd = Command(tx, """
            UPDATE PrintJobs
            SET Status=CASE WHEN $status='retry' THEN 'queued' ELSE $status END,
                AvailableAt=$now+60, LeaseUntil=NULL, Reason=$reason,
                CompletedAt=CASE WHEN $status='retry' THEN NULL ELSE $now END,
                CustomerName=CASE WHEN $status IN ('sent','uncertain') THEN '' ELSE CustomerName END,
                SpecialRequests=CASE WHEN $status IN ('sent','uncertain') THEN '' ELSE SpecialRequests END
            WHERE Id=$id AND AgentId=$agent AND Lease=$lease AND Status='leased'
            """, ("$status", status), ("$now", now), ("$reason", reason), ("$id", id), ("$agent", agent), ("$lease", lease));
        bool accepted = cmd.ExecuteNonQuery() == 1;
        if (!accepted)
        {
            using SqliteCommand replay = Command(tx, """
                SELECT 1 FROM PrintJobs WHERE Id=$id AND AgentId=$agent AND Lease=$lease
                AND Status=CASE WHEN $status='retry' THEN 'queued' ELSE $status END
                """, ("$id", id), ("$agent", agent), ("$lease", lease), ("$status", status));
            accepted = replay.ExecuteScalar() is not null;
        }
        tx.Commit();
        return accepted ? ResultCode.Accepted : ResultCode.Conflict;
    }

    private static string Line(string s) => string.Concat(s.Where(c => !char.IsControl(c))).Trim();

    internal static string Receipt(string kind, string reference, string name, int seats, string? date, string timezone, string restaurant, string locale, string requests)
    {
        // UTC is stored in SQLite; print on the restaurant clock, never on the server's clock.
        string[] words = locale switch
        {
            "nl" => ["NIEUWE RESERVERING","GEANNULEERD","GEWIJZIGD","TESTBON","Gasten","Naam","Referentie"],
            "fr" => ["NOUVELLE RESERVATION","ANNULEE","MODIFIEE","TEST","Convives","Nom","Reference"],
            "de" => ["NEUE RESERVIERUNG","STORNIERT","GEAENDERT","TESTBON","Gaeste","Name","Referenz"],
            "es" => ["NUEVA RESERVA","CANCELADA","MODIFICADA","PRUEBA","Personas","Nombre","Referencia"],
            _ => ["NEW BOOKING","CANCELLED","CHANGED","TEST RECEIPT","Guests","Name","Reference"],
        };
        int heading = kind switch { "created" => 0, "cancelled" => 1, "changed" => 2, _ => 3 };
        if (kind == "test")
        {
            return $"{words[heading]}\n{Line(restaurant)}\nOpenResto Print Agent";
        }
        DateTime utc = DateTime.Parse(date ?? throw new InvalidOperationException("Missing booking date."), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        DateTime local = TimeZoneHelper.ConvertUtcToLocal(utc, timezone);
        string notes = locale switch { "nl" => "Opmerking", "fr" => "Remarque", "de" => "Hinweis", "es" => "Nota", _ => "Note" };
        string receipt = string.Create(CultureInfo.InvariantCulture, $"{words[heading]}\n{Line(restaurant)}\n--------------------------------\n{local:dd-MM-yyyy HH:mm}\n{words[4]}: {seats}\n{words[5]}: {Line(name)}\n{words[6]}: {Line(reference)}");
        return string.IsNullOrWhiteSpace(requests) ? receipt : $"{receipt}\n{notes}: {Line(requests)}";
    }
}
