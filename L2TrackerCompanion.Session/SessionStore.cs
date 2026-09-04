using System.Globalization;
using System.Text;
using System.Text.Json;
using L2TrackerCompanion.Parsing;
using Microsoft.Data.Sqlite;

namespace L2TrackerCompanion.Session;

/// <summary>
/// Append-only SQLite snapshots for the active session (plan step 14).
/// <see cref="NewSession"/> wipes the file when the player resets the
/// Play Report in-game, which is what now starts a session.
/// </summary>
public sealed class SessionStore : IDisposable
{
    public const string AppDataFolderName = "L2TrackerCompanion";
    public const string DefaultFileName = "session.db";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Rejections in a row against the same stored baseline before that
    /// baseline is treated as the stale thing and dropped.
    /// </summary>
    /// <remarks>
    /// A real OCR misread is transient — the next tick recovers. Several
    /// rejections in a row all measured against one unchanged row are evidence
    /// that the row is out of date (a reset nobody was watching for), not that
    /// the reads are bad. Without this the buffer can never advance again.
    /// </remarks>
    public const int StaleBaselineStrikes = 3;

    private readonly string _path;
    private SqliteConnection _connection;
    private int _consecutiveRejections;

    public string Path => _path;

    public SessionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        if (!string.Equals(path, ":memory:", StringComparison.Ordinal))
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
                location_hint, unread_fields, warnings,
                xp_disagreed, xp_spliced, xp_magnitude_mismatch, adena_disagreed, play_time_disagreed,
                xp_from_tokens, xp_from_crop, adena_from_tokens, adena_from_crop)
            VALUES (
                $captured_at, $xp, $adena, $minutes,
                $red, $purple, $blue, $green,
                $lamp_read, $lamp_closed, $lamp_exceeds, $lamp_total,
                $hint, $unread, $warnings,
                $xp_disagreed, $xp_spliced, $xp_magnitude_mismatch, $adena_disagreed, $play_time_disagreed,
                $xp_from_tokens, $xp_from_crop, $adena_from_tokens, $adena_from_crop);
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
        command.Parameters.AddWithValue("$xp_disagreed", report.Confidence.XpDisagreed ? 1 : 0);
        command.Parameters.AddWithValue("$xp_spliced", report.Confidence.XpSpliced ? 1 : 0);
        command.Parameters.AddWithValue("$xp_magnitude_mismatch", report.Confidence.XpMagnitudeMismatch ? 1 : 0);
        command.Parameters.AddWithValue("$adena_disagreed", report.Confidence.AdenaDisagreed ? 1 : 0);
        command.Parameters.AddWithValue("$play_time_disagreed", report.Confidence.PlayTimeDisagreed ? 1 : 0);
        BindLong(command, "$xp_from_tokens", report.Confidence.XpFromTokens);
        BindLong(command, "$xp_from_crop", report.Confidence.XpFromCrop);
        BindLong(command, "$adena_from_tokens", report.Confidence.AdenaFromTokens);
        BindLong(command, "$adena_from_crop", report.Confidence.AdenaFromCrop);

        var id = (long)command.ExecuteScalar()!;
        return new SnapshotRow(id, at, report);
    }

    public SnapshotRow? Last()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, captured_at, xp, adena, minutes,
                   red_lamp_xp, purple_lamp_xp, blue_lamp_xp, green_lamp_xp,
                   lamp_xp_read, lamp_panel_closed, lamp_xp_exceeds_dialog, lamp_xp_total,
                   location_hint, unread_fields, warnings,
                   xp_disagreed, xp_spliced, xp_magnitude_mismatch, adena_disagreed, play_time_disagreed,
                   xp_from_tokens, xp_from_crop, adena_from_tokens, adena_from_crop
            FROM snapshots
            ORDER BY id DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    /// <summary>
    /// Append <paramref name="report"/> only when it does not drop versus the
    /// last accepted snapshot. Used by the polling loop and by one-shot parse.
    /// </summary>
    public SnapshotAcceptResult TryAccept(PlayReport report, DateTimeOffset? capturedAt = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var decision = Monotonicity.Evaluate(Last()?.Report, report);
        if (decision.Outcome == MonotonicityOutcome.Misread)
        {
            _consecutiveRejections++;
            if (_consecutiveRejections < StaleBaselineStrikes)
            {
                return SnapshotAcceptResult.Discarded(decision.Reason!);
            }

            // Nothing has been accepted for several ticks running: the baseline
            // is what is wrong, not the reads.
            NewSession();
            return SnapshotAcceptResult.AfterReset(
                Append(report, capturedAt),
                $"No read matched the previous one for {StaleBaselineStrikes} ticks — "
                + "the stored baseline was dropped and counting restarted.");
        }

        _consecutiveRejections = 0;
        if (decision.IsReset)
        {
            // The player restarted the Play Report in-game, which is how a
            // session now begins. Everything buffered belongs to the previous
            // one, so it goes rather than being compared against the new run.
            NewSession();
            return SnapshotAcceptResult.AfterReset(Append(report, capturedAt), decision.Reason!);
        }

        return SnapshotAcceptResult.Accepted(Append(report, capturedAt));
    }

    public IReadOnlyList<SnapshotRow> List()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, captured_at, xp, adena, minutes,
                   red_lamp_xp, purple_lamp_xp, blue_lamp_xp, green_lamp_xp,
                   lamp_xp_read, lamp_panel_closed, lamp_xp_exceeds_dialog, lamp_xp_total,
                   location_hint, unread_fields, warnings,
                   xp_disagreed, xp_spliced, xp_magnitude_mismatch, adena_disagreed, play_time_disagreed,
                   xp_from_tokens, xp_from_crop, adena_from_tokens, adena_from_crop
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
    /// Drop the comparison buffer. Clearing happens on in-game reset, client
    /// restart, a stale baseline, and pressing Start.
    /// </summary>
    /// <remarks>
    /// This used to delete the database file, which meant closing the
    /// connection first and left the store unusable whenever the delete
    /// failed. A DELETE keeps the connection alive and cannot half-succeed.
    /// The sqlite_sequence row goes with it: id is declared AUTOINCREMENT,
    /// whose whole job is to never reuse a value, so deleting the rows alone
    /// left the next session counting on from the old one — "Accepted #47"
    /// on the first read of a fresh session. Nothing depends on ids being
    /// unique across sessions; they only order rows within the current one.
    /// </remarks>
    public void NewSession()
    {
        _consecutiveRejections = 0;
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM snapshots;
            DELETE FROM sqlite_sequence WHERE name = 'snapshots';
            """;
        command.ExecuteNonQuery();
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
                warnings TEXT NOT NULL,
                xp_disagreed INTEGER NOT NULL DEFAULT 0,
                xp_spliced INTEGER NOT NULL DEFAULT 0,
                xp_magnitude_mismatch INTEGER NOT NULL DEFAULT 0,
                adena_disagreed INTEGER NOT NULL DEFAULT 0,
                play_time_disagreed INTEGER NOT NULL DEFAULT 0,
                xp_from_tokens INTEGER,
                xp_from_crop INTEGER,
                adena_from_tokens INTEGER,
                adena_from_crop INTEGER
            );

            DROP TABLE IF EXISTS saved_logs;
            """;
        command.ExecuteNonQuery();
        AddMissingColumns(connection);
        return connection;
    }

    /// <summary>
    /// <c>CREATE TABLE IF NOT EXISTS</c> leaves a pre-existing session file on
    /// its old shape, so add the in-frame agreement columns to databases that
    /// were created before they existed.
    /// </summary>
    private static void AddMissingColumns(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(snapshots);";
            using var reader = probe.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(1));
            }
        }

        string[] added =
        [
            "xp_disagreed",
            "xp_spliced",
            "xp_magnitude_mismatch",
            "adena_disagreed",
            "play_time_disagreed",
        ];
        foreach (var column in added)
        {
            if (existing.Contains(column))
            {
                continue;
            }

            using var alter = connection.CreateCommand();
            alter.CommandText =
                $"ALTER TABLE snapshots ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }

        string[] nullableAdded =
        [
            "xp_from_tokens",
            "xp_from_crop",
            "adena_from_tokens",
            "adena_from_crop",
        ];
        foreach (var column in nullableAdded)
        {
            if (existing.Contains(column))
            {
                continue;
            }

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE snapshots ADD COLUMN {column} INTEGER;";
            alter.ExecuteNonQuery();
        }
    }

    private static SnapshotRow ReadRow(SqliteDataReader reader)
    {
        var capturedAt = DateTimeOffset.Parse(
            reader.GetString(1),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var unread = JsonSerializer.Deserialize<List<string>>(reader.GetString(14), JsonOptions) ?? [];
        var warnings = JsonSerializer.Deserialize<List<string>>(reader.GetString(15), JsonOptions) ?? [];
        var confidence = new ReadConfidence(
            XpDisagreed: reader.GetInt32(16) != 0,
            XpSpliced: reader.GetInt32(17) != 0,
            XpMagnitudeMismatch: reader.GetInt32(18) != 0,
            AdenaDisagreed: reader.GetInt32(19) != 0,
            PlayTimeDisagreed: reader.GetInt32(20) != 0,
            XpFromTokens: GetNullableInt64(reader, 21),
            XpFromCrop: GetNullableInt64(reader, 22),
            AdenaFromTokens: GetNullableInt64(reader, 23),
            AdenaFromCrop: GetNullableInt64(reader, 24));
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
            Warnings: warnings,
            Confidence: confidence);
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

public sealed record SnapshotAcceptResult(
    bool Appended,
    string? Reason,
    SnapshotRow? Row,
    MonotonicityOutcome Outcome = MonotonicityOutcome.Accepted)
{
    public bool WasReset => Outcome == MonotonicityOutcome.Reset;

    public static SnapshotAcceptResult Accepted(SnapshotRow row) => new(true, null, row);

    public static SnapshotAcceptResult AfterReset(SnapshotRow row, string reason)
        => new(true, reason, row, MonotonicityOutcome.Reset);

    public static SnapshotAcceptResult Discarded(string reason)
        => new(false, reason, null, MonotonicityOutcome.Misread);
}
