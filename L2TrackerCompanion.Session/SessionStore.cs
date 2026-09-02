using System.Globalization;
using System.Text;
using System.Text.Json;
using L2TrackerCompanion.Parsing;
using Microsoft.Data.Sqlite;

namespace L2TrackerCompanion.Session;

/// <summary>
/// Append-only SQLite snapshots for the active session (plan step 14).
/// Clear-on-save is later; <see cref="NewSession"/> wipes the file now.
/// </summary>
public sealed class SessionStore : IDisposable
{
    public const string AppDataFolderName = "L2TrackerCompanion";
    public const string DefaultFileName = "session.db";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly bool _isMemory;
    private SqliteConnection _connection;

    public string Path => _path;

    public SessionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _isMemory = string.Equals(path, ":memory:", StringComparison.Ordinal);
        if (!_isMemory)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        _connection = OpenAndMigrate(_path);
    }

    public static string GetDefaultPath()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName);
        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, DefaultFileName);
    }

    public SnapshotRow Append(PlayReport report, DateTimeOffset? capturedAt = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var at = (capturedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();

        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO snapshots (
                captured_at, xp, adena, minutes,
                red_lamp_xp, purple_lamp_xp, blue_lamp_xp, green_lamp_xp,
                lamp_xp_read, lamp_panel_closed, lamp_xp_exceeds_dialog, lamp_xp_total,
                location_hint, unread_fields, warnings)
            VALUES (
                $captured_at, $xp, $adena, $minutes,
                $red, $purple, $blue, $green,
                $lamp_read, $lamp_closed, $lamp_exceeds, $lamp_total,
                $hint, $unread, $warnings);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$captured_at", at.ToString("o", CultureInfo.InvariantCulture));
        BindLong(command, "$xp", report.Xp);
        BindLong(command, "$adena", report.Adena);
        BindLong(command, "$minutes", report.Minutes);
        BindLong(command, "$red", report.RedLampXp);
        BindLong(command, "$purple", report.PurpleLampXp);
        BindLong(command, "$blue", report.BlueLampXp);
        BindLong(command, "$green", report.GreenLampXp);
        command.Parameters.AddWithValue("$lamp_read", report.LampXpRead ? 1 : 0);
        command.Parameters.AddWithValue("$lamp_closed", report.LampPanelClosed ? 1 : 0);
        command.Parameters.AddWithValue("$lamp_exceeds", report.LampXpExceedsDialog ? 1 : 0);
        command.Parameters.AddWithValue("$lamp_total", report.LampXpTotal);
        command.Parameters.AddWithValue("$hint", (object?)report.LocationHint ?? DBNull.Value);
        command.Parameters.AddWithValue("$unread", JsonSerializer.Serialize(report.UnreadFields, JsonOptions));
        command.Parameters.AddWithValue("$warnings", JsonSerializer.Serialize(report.Warnings, JsonOptions));

        var id = (long)command.ExecuteScalar()!;
        return new SnapshotRow(id, at, report);
    }

    public IReadOnlyList<SnapshotRow> List()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, captured_at, xp, adena, minutes,
                   red_lamp_xp, purple_lamp_xp, blue_lamp_xp, green_lamp_xp,
                   lamp_xp_read, lamp_panel_closed, lamp_xp_exceeds_dialog, lamp_xp_total,
                   location_hint, unread_fields, warnings
            FROM snapshots
            ORDER BY id ASC;
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<SnapshotRow>();
        while (reader.Read())
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    public int Count
    {
        get
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM snapshots;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Wipe the active session. File-backed stores delete the db and open a
    /// new empty one; in-memory stores just delete the rows.
    /// </summary>
    public void NewSession()
    {
        if (_isMemory)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM snapshots;";
            command.ExecuteNonQuery();
            return;
        }

        _connection.Dispose();
        SqliteConnection.ClearPool(_connection);
        File.Delete(_path);
        var wal = _path + "-wal";
        var shm = _path + "-shm";
        if (File.Exists(wal))
        {
            File.Delete(wal);
        }

        if (File.Exists(shm))
        {
            File.Delete(shm);
        }

        _connection = OpenAndMigrate(_path);
    }

    public static string FormatInspect(IReadOnlyList<SnapshotRow> rows, string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Session: {rows.Count} snapshot{(rows.Count == 1 ? "" : "s")}");
        builder.AppendLine(path);
        var inv = CultureInfo.InvariantCulture;
        foreach (var row in rows)
        {
            var report = row.Report;
            var lamps = report.LampPanelClosed ? "closed"
                : report.LampXpExceedsDialog ? "discarded"
                : report.LampXpRead ? "read"
                : "unread";
            builder.Append('#');
            builder.Append(row.Id.ToString(inv));
            builder.Append("  ");
            builder.Append(row.CapturedAt.UtcDateTime.ToString("HH:mm:ss", inv));
            builder.Append("  xp=");
            builder.Append(Amt(report.Xp, inv));
            builder.Append("  adena=");
            builder.Append(Amt(report.Adena, inv));
            builder.Append("  ");
            builder.Append(report.Minutes is null ? "(no time)" : report.Minutes.Value.ToString(inv) + " min");
            builder.Append("  lamps=");
            builder.Append(lamps);
            if (report.LocationHint is not null)
            {
                builder.Append("  @ ");
                builder.Append(report.LocationHint);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearPool(_connection);
    }

    private static SqliteConnection OpenAndMigrate(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                captured_at TEXT NOT NULL,
                xp INTEGER,
                adena INTEGER,
                minutes INTEGER,
                red_lamp_xp INTEGER,
                purple_lamp_xp INTEGER,
                blue_lamp_xp INTEGER,
                green_lamp_xp INTEGER,
                lamp_xp_read INTEGER NOT NULL,
                lamp_panel_closed INTEGER NOT NULL,
                lamp_xp_exceeds_dialog INTEGER NOT NULL,
                lamp_xp_total INTEGER NOT NULL,
                location_hint TEXT,
                unread_fields TEXT NOT NULL,
                warnings TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static SnapshotRow ReadRow(SqliteDataReader reader)
    {
        var capturedAt = DateTimeOffset.Parse(
            reader.GetString(1),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var unread = JsonSerializer.Deserialize<List<string>>(reader.GetString(14), JsonOptions) ?? [];
        var warnings = JsonSerializer.Deserialize<List<string>>(reader.GetString(15), JsonOptions) ?? [];
        var report = new PlayReport(
            Xp: GetNullableInt64(reader, 2),
            Adena: GetNullableInt64(reader, 3),
            Minutes: GetNullableInt32(reader, 4),
            RedLampXp: GetNullableInt64(reader, 5),
            PurpleLampXp: GetNullableInt64(reader, 6),
            BlueLampXp: GetNullableInt64(reader, 7),
            GreenLampXp: GetNullableInt64(reader, 8),
            LampXpRead: reader.GetInt32(9) != 0,
            LampPanelClosed: reader.GetInt32(10) != 0,
            LampXpExceedsDialog: reader.GetInt32(11) != 0,
            LampXpTotal: reader.GetInt64(12),
            LocationHint: reader.IsDBNull(13) ? null : reader.GetString(13),
            UnreadFields: unread,
            Warnings: warnings);
        return new SnapshotRow(reader.GetInt64(0), capturedAt, report);
    }

    private static void BindLong(SqliteCommand command, string name, long? value)
        => command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value);

    private static long? GetNullableInt64(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string Amt(long? value, CultureInfo inv)
        => value is null ? "(unread)" : value.Value.ToString(inv);
}

public sealed record SnapshotRow(long Id, DateTimeOffset CapturedAt, PlayReport Report);
