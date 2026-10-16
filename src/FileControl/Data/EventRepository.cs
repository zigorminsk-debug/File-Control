using FileControl.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace FileControl.Data;

public sealed class EventRepository
{
    private readonly string _connectionString;
    public EventRepository(IOptions<FileControlOptions> options, IWebHostEnvironment env)
    {
        var path = options.Value.DatabasePath;
        if (!Path.IsPathRooted(path)) path = Path.Combine(env.ContentRootPath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var db = new SqliteConnection(_connectionString); await db.OpenAsync();
        var sql = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS file_events (
          id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp TEXT NOT NULL, user TEXT NOT NULL,
          domain TEXT NOT NULL, path TEXT NOT NULL, document_name TEXT NOT NULL,
          action TEXT NOT NULL, process TEXT NULL, details TEXT NULL, event_record_id INTEGER NOT NULL UNIQUE);
        CREATE INDEX IF NOT EXISTS ix_events_time ON file_events(timestamp);
        CREATE INDEX IF NOT EXISTS ix_events_user ON file_events(user);
        CREATE INDEX IF NOT EXISTS ix_events_name ON file_events(document_name);
        -- Remove disposable editor/cache files collected by older versions.
        DELETE FROM file_events WHERE lower(document_name) GLOB '*.tmp'
          OR lower(document_name) GLOB '*.temp' OR lower(document_name) GLOB '*.bak'
          OR lower(document_name) GLOB '*.swp' OR lower(document_name) GLOB '*.part'
          OR lower(document_name) GLOB '*.crdownload' OR document_name LIKE '~%'
          OR lower(document_name) IN ('thumbs.db','desktop.ini');
        """;
        await new SqliteCommand(sql, db).ExecuteNonQueryAsync();
    }

    public async Task AddAsync(FileEventRecord e)
    {
        await using var db = new SqliteConnection(_connectionString); await db.OpenAsync();
        var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO file_events(timestamp,user,domain,path,document_name,action,process,details,event_record_id) VALUES($t,$u,$d,$p,$n,$a,$pr,$x,$r)";
        cmd.Parameters.AddWithValue("$t", e.Timestamp.ToUniversalTime().ToString("O")); cmd.Parameters.AddWithValue("$u", e.User);
        cmd.Parameters.AddWithValue("$d", e.Domain); cmd.Parameters.AddWithValue("$p", e.Path); cmd.Parameters.AddWithValue("$n", e.DocumentName);
        cmd.Parameters.AddWithValue("$a", e.Action); cmd.Parameters.AddWithValue("$pr", (object?)e.Process ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$x", (object?)e.Details ?? DBNull.Value); cmd.Parameters.AddWithValue("$r", e.EventRecordId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<FileEventRecord>> QueryAsync(DateTimeOffset from, DateTimeOffset to, string? user, string? document, int limit = 5000)
    {
        await using var db = new SqliteConnection(_connectionString); await db.OpenAsync(); var cmd = db.CreateCommand();
        cmd.CommandText = """
          SELECT id,timestamp,user,domain,path,document_name,action,process,details,event_record_id FROM file_events
          WHERE timestamp >= $from AND timestamp < $to AND ($user='' OR user LIKE $user) AND ($doc='' OR document_name LIKE $doc OR path LIKE $doc)
          ORDER BY timestamp DESC LIMIT $limit
          """;
        cmd.Parameters.AddWithValue("$from", from.ToUniversalTime().ToString("O")); cmd.Parameters.AddWithValue("$to", to.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(user) ? "" : $"%{user}%"); cmd.Parameters.AddWithValue("$doc", string.IsNullOrWhiteSpace(document) ? "" : $"%{document}%"); cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 20000));
        var rows = new List<FileEventRecord>(); await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) rows.Add(new(r.GetInt64(0), DateTimeOffset.Parse(r.GetString(1)), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.IsDBNull(7)?null:r.GetString(7), r.IsDBNull(8)?null:r.GetString(8), r.GetInt64(9)));
        return rows;
    }

    public async Task PurgeAsync(DateTimeOffset before)
    { await using var db = new SqliteConnection(_connectionString); await db.OpenAsync(); var cmd=db.CreateCommand(); cmd.CommandText="DELETE FROM file_events WHERE timestamp < $x"; cmd.Parameters.AddWithValue("$x",before.ToUniversalTime().ToString("O")); await cmd.ExecuteNonQueryAsync(); }
}
