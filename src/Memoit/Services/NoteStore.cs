using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Memoit.Models;

namespace Memoit.Services;

public sealed class NoteStore(string databasePath) : IDisposable
{
    private const int ApplicationId = 0x4D454D49;
    private const int SchemaVersion = 2;
    private readonly string path = Path.GetFullPath(databasePath);
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;
    private const string Columns = "id, body, color, font_size, font_family, is_collapsed, left_pos, top_pos, width, height, is_visible, is_pinned, created_at, updated_at, deleted_at";

    public Task InitializeAsync() => Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var db = Open(path);
        var version = Scalar(db, "PRAGMA user_version");
        if (version == 0 && Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='table'") == 0
            && Scalar(db, "PRAGMA application_id") == 0)
        {
            using var tx = db.BeginTransaction();
            Execute(db, "CREATE TABLE notes (id TEXT PRIMARY KEY, body TEXT NOT NULL, color TEXT NOT NULL, font_size REAL NOT NULL, font_family TEXT NOT NULL DEFAULT 'Malgun Gothic', is_collapsed INTEGER NOT NULL DEFAULT 0, left_pos REAL NOT NULL, top_pos REAL NOT NULL, width REAL NOT NULL, height REAL NOT NULL, is_visible INTEGER NOT NULL, is_pinned INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, deleted_at TEXT)", tx);
            Execute(db, $"PRAGMA application_id={ApplicationId}; PRAGMA user_version={SchemaVersion}", tx);
            tx.Commit();
        }
        if (version > SchemaVersion) throw new InvalidDataException("지원하지 않는 데이터베이스 버전입니다. 파일을 변경하지 않았습니다.");
        if (version == 1)
        {
            ValidateApplicationId(db);
            ValidateQuickCheck(db);
            _ = ReadNotesV1(db); // validate every row before any mutation
            BackupRaw(db, Path.Combine(Path.GetDirectoryName(path)!, "backups", $"before-migration-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"));
            using var tx = db.BeginTransaction();
            Execute(db, "ALTER TABLE notes ADD COLUMN font_family TEXT NOT NULL DEFAULT 'Malgun Gothic'", tx);
            Execute(db, "ALTER TABLE notes ADD COLUMN is_collapsed INTEGER NOT NULL DEFAULT 0", tx);
            Execute(db, $"PRAGMA user_version={SchemaVersion}", tx);
            tx.Commit();
        }
        ValidateDatabase(db);
    });

    public Task<IReadOnlyList<Note>> LoadAsync() => Run<IReadOnlyList<Note>>(() =>
    {
        using var db = Open(path, true);
        ValidateDatabase(db);
        return ReadNotes(db);
    });

    public Task SaveAsync(Note note) => Run(() =>
    {
        ValidateNote(note);
        using var db = Open(path);
        ValidateHeader(db);
        using var tx = db.BeginTransaction();
        WriteNote(db, tx, note);
        tx.Commit();
    });

    public Task DeletePermanentlyAsync(Guid id) => Run(() =>
    {
        using var db = Open(path);
        ValidateHeader(db);
        using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM notes WHERE id=$id AND deleted_at IS NOT NULL";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    });

    public Task BackupAsync(string destination) => Run(() => BackupCore(destination));

    public Task<string?> CreateDailyBackupAsync(string directory) => Run<string?>(() =>
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, $"memoit-{DateTime.Now:yyyy-MM-dd}.db");
        if (File.Exists(target)) return null;
        BackupCore(target);
        foreach (var old in Directory.GetFiles(directory, "memoit-????-??-??.db")
            .Where(p => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(p)[7..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(Path.GetFileName).Skip(7)) File.Delete(old);
        return target;
    });

    public Task RestoreAsync(string source) => Run(() =>
    {
        if (SamePath(source, path)) throw new InvalidOperationException("현재 데이터 파일을 복원 원본으로 사용할 수 없습니다.");
        List<Note> notes;
        using (var incoming = Open(source, true))
        {
            long version = Scalar(incoming, "PRAGMA user_version");
            if (version == 1) { ValidateApplicationId(incoming); ValidateQuickCheck(incoming); notes = ReadNotesV1(incoming); }
            else { ValidateDatabase(incoming); notes = ReadNotes(incoming); }
        }
        BackupCore(Path.Combine(Path.GetDirectoryName(path)!, "backups", $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"));
        using var db = Open(path);
        ValidateHeader(db);
        using var tx = db.BeginTransaction();
        Execute(db, "DELETE FROM notes", tx);
        foreach (var note in notes) WriteNote(db, tx, note);
        tx.Commit();
    });

    private void BackupCore(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (SamePath(destination, path)) throw new InvalidOperationException("현재 데이터 파일을 백업 대상으로 사용할 수 없습니다.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var original = Open(path, true))
            using (var backup = Open(temporary))
            {
                ValidateHeader(original);
                original.BackupDatabase(backup);
                ValidateDatabase(backup);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void BackupRaw(SqliteConnection original, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var backup = Open(temporary))
            {
                original.BackupDatabase(backup);
                ValidateApplicationId(backup);
                ValidateQuickCheck(backup);
                _ = ReadNotesV1(backup);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static SqliteConnection Open(string file, bool readOnly = false)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        try { db.Open(); return db; }
        catch { db.Dispose(); throw; }
    }

    private static void ValidateHeader(SqliteConnection db)
    {
        if (Scalar(db, "PRAGMA user_version") != SchemaVersion) throw new InvalidDataException("지원하지 않는 데이터베이스 버전입니다. 파일을 변경하지 않았습니다.");
        ValidateApplicationId(db);
    }
    private static void ValidateApplicationId(SqliteConnection db) { if (Scalar(db, "PRAGMA application_id") != ApplicationId) throw new InvalidDataException("OmniMemo 데이터베이스가 아닙니다."); }
    private static void ValidateQuickCheck(SqliteConnection db) { using var c = db.CreateCommand(); c.CommandText = "PRAGMA quick_check"; if (!string.Equals(c.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal)) throw new InvalidDataException("데이터베이스가 손상되었습니다."); }

    private static void ValidateDatabase(SqliteConnection db)
    {
        ValidateHeader(db);
        ValidateQuickCheck(db);
        _ = ReadNotes(db);
    }

    private static List<Note> ReadNotesV1(SqliteConnection db)
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT id,body,color,font_size,left_pos,top_pos,width,height,is_visible,is_pinned,created_at,updated_at,deleted_at FROM notes ORDER BY updated_at DESC";
        using var reader = command.ExecuteReader(); var notes = new List<Note>();
        while (reader.Read())
        {
            if (reader.GetInt32(8) is not (0 or 1) || reader.GetInt32(9) is not (0 or 1)) throw new InvalidDataException("메모 표시 상태가 올바르지 않습니다.");
            var n = new Note { Id=Guid.Parse(reader.GetString(0)), Body=reader.GetString(1), Color=reader.GetString(2), FontSize=reader.GetDouble(3), Left=reader.GetDouble(4), Top=reader.GetDouble(5), Width=reader.GetDouble(6), Height=reader.GetDouble(7), IsVisible=reader.GetBoolean(8), IsPinned=reader.GetBoolean(9), CreatedAt=ParseTime(reader.GetString(10)), UpdatedAt=ParseTime(reader.GetString(11)), DeletedAt=reader.IsDBNull(12)?null:ParseTime(reader.GetString(12)) };
            ValidateNote(n); notes.Add(n);
        }
        return notes.OrderByDescending(n => n.UpdatedAt).ToList();
    }

    private static List<Note> ReadNotes(SqliteConnection db)
    {
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM notes ORDER BY updated_at DESC";
        using var reader = command.ExecuteReader();
        var notes = new List<Note>();
        while (reader.Read())
        {
            if (reader.GetInt32(5) is not (0 or 1) || reader.GetInt32(10) is not (0 or 1) || reader.GetInt32(11) is not (0 or 1)) throw new InvalidDataException("메모 표시 상태가 올바르지 않습니다.");
            var note = new Note
            {
                Id = Guid.Parse(reader.GetString(0)), Body = reader.GetString(1), Color = reader.GetString(2), FontSize = reader.GetDouble(3), FontFamily = reader.GetString(4), IsCollapsed = reader.GetBoolean(5),
                Left = reader.GetDouble(6), Top = reader.GetDouble(7), Width = reader.GetDouble(8), Height = reader.GetDouble(9),
                IsVisible = reader.GetBoolean(10), IsPinned = reader.GetBoolean(11), CreatedAt = ParseTime(reader.GetString(12)),
                UpdatedAt = ParseTime(reader.GetString(13)), DeletedAt = reader.IsDBNull(14) ? null : ParseTime(reader.GetString(14))
            };
            ValidateNote(note);
            notes.Add(note);
        }
        return notes.OrderByDescending(n => n.UpdatedAt).ToList();
    }

    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
    private static void ValidateNote(Note note)
    {
        if (note.Id == Guid.Empty || note.Body is null || note.Color is null || !Regex.IsMatch(note.Color, "^#[0-9a-fA-F]{6}$")
            || !double.IsFinite(note.Left) || !double.IsFinite(note.Top) || !double.IsFinite(note.Width) || !double.IsFinite(note.Height)
            || !double.IsFinite(note.FontSize) || note.Width < 200 || note.Height < 150 || note.FontSize < 10 || note.FontSize > 72
            || string.IsNullOrWhiteSpace(note.FontFamily) || note.FontFamily.Length > 100)
            throw new InvalidDataException("메모의 내용 또는 창 설정이 올바르지 않습니다.");
    }

    private static void WriteNote(SqliteConnection db, SqliteTransaction tx, Note note)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT OR REPLACE INTO notes ({Columns}) VALUES ($id,$body,$color,$font,$family,$collapsed,$left,$top,$width,$height,$visible,$pinned,$created,$updated,$deleted)";
        object[] values = [note.Id.ToString(), note.Body, note.Color, note.FontSize, note.FontFamily, note.IsCollapsed, note.Left, note.Top, note.Width, note.Height, note.IsVisible, note.IsPinned, note.CreatedAt.ToString("O"), note.UpdatedAt.ToString("O"), (object?)note.DeletedAt?.ToString("O") ?? DBNull.Value];
        string[] names = ["id", "body", "color", "font", "family", "collapsed", "left", "top", "width", "height", "visible", "pinned", "created", "updated", "deleted"];
        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("$" + names[i], values[i]);
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar());
    }
    private static void Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        using var command = db.CreateCommand(); command.CommandText = sql; command.Transaction = tx; command.ExecuteNonQuery();
    }
    private Task Run(Action action) => Run(() => { action(); return true; });
    private async Task<T> Run<T>(Func<T> action)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(disposed, this); return await Task.Run(action).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    public void Dispose() { gate.Wait(); try { disposed = true; } finally { gate.Release(); } }
}
