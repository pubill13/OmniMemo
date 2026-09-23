using Memoit.Models;
using Memoit.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Memoit.Tests;

public sealed class NoteStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MemoitTests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "notes.db");
    public NoteStoreTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public async Task SavesAndReloadsUnicodeAndWindowState()
    {
        var note = new Note { Body = "\n 한글 받침 테스트\n두 번째 줄 😀", Left = -500, IsPinned = true, Color = "#FFCCDD", FontSize = 24 };
        using (var store = new NoteStore(Database)) { await store.InitializeAsync(); await store.SaveAsync(note); }
        using var reopened = new NoteStore(Database);
        await reopened.InitializeAsync();
        Assert.Equal(note, Assert.Single(await reopened.LoadAsync()));
        Assert.Equal("한글 받침 테스트", note.Title);
    }

    [Fact]
    public async Task OrderedSavesKeepLastContent()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var note = new Note();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => store.SaveAsync(note with { Body = i.ToString() })));
        Assert.Equal("99", Assert.Single(await store.LoadAsync()).Body);
    }

    [Fact]
    public async Task OnlyTrashCanBePermanentlyDeleted()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var note = new Note();
        await store.SaveAsync(note);
        await store.DeletePermanentlyAsync(note.Id);
        Assert.Single(await store.LoadAsync());
        await store.SaveAsync(note with { DeletedAt = DateTimeOffset.UtcNow });
        await store.DeletePermanentlyAsync(note.Id);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task BackupRestoreReplacesAllDataAndKeepsSafetyCopy()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var original = new Note { Body = "백업 원본" };
        await store.SaveAsync(original);
        var backup = Path.Combine(directory, "copy.db");
        await store.BackupAsync(backup);
        await store.SaveAsync(original with { Body = "새 내용" });
        await store.SaveAsync(new Note());
        await store.RestoreAsync(backup);
        Assert.Equal(original, Assert.Single(await store.LoadAsync()));
        var safety = Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups"), "before-restore-*.db"));
        using var previous = new NoteStore(safety);
        Assert.Equal(2, (await previous.LoadAsync()).Count);
    }

    [Fact]
    public async Task InvalidRestoreAndSavePreserveExistingData()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var note = new Note { Body = "보존" };
        await store.SaveAsync(note);
        var corrupt = Path.Combine(directory, "broken.db");
        await File.WriteAllTextAsync(corrupt, "not a database");
        await Assert.ThrowsAnyAsync<Exception>(() => store.RestoreAsync(corrupt));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(note with { Left = double.NaN }));
        Assert.Equal(note, Assert.Single(await store.LoadAsync()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.BackupAsync(Database));
    }

    [Fact]
    public async Task FutureVersionIsRejectedWithoutChangingIt()
    {
        using (var store = new NoteStore(Database)) await store.InitializeAsync();
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery();
        }
        using var future = new NoteStore(Database);
        await Assert.ThrowsAsync<InvalidDataException>(() => future.InitializeAsync());
        using var verify = new SqliteConnection($"Data Source={Database};Pooling=False");
        verify.Open(); using var check = verify.CreateCommand(); check.CommandText = "PRAGMA user_version";
        Assert.Equal(99L, check.ExecuteScalar());
    }

    [Fact]
    public async Task DailyBackupKeepsSevenAndDoesNotOverwriteToday()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var backups = Path.Combine(directory, "daily");
        Directory.CreateDirectory(backups);
        for (int day = 1; day <= 9; day++) await File.WriteAllTextAsync(Path.Combine(backups, $"memoit-{DateTime.Now.AddDays(-day):yyyy-MM-dd}.db"), "old");
        Assert.NotNull(await store.CreateDailyBackupAsync(backups));
        Assert.Equal(7, Directory.GetFiles(backups).Length);
        Assert.Null(await store.CreateDailyBackupAsync(backups));
    }

    [Fact]
    public async Task InvalidNoteInBackupDoesNotReplaceLiveData()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var note = new Note { Body = "live" };
        await store.SaveAsync(note);
        string backup = Path.Combine(directory, "invalid.db");
        await store.BackupAsync(backup);
        using (var db = new SqliteConnection($"Data Source={backup};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE notes SET width=-1"; cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RestoreAsync(backup));
        Assert.Equal(note, Assert.Single(await store.LoadAsync()));
    }

    [Fact]
    public async Task MigratesV1WithPreMigrationBackupAndDefaults()
    {
        CreateV1(Database, new Note { Body = "구버전" });
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var note = Assert.Single(await store.LoadAsync());
        Assert.Equal("Malgun Gothic", note.FontFamily);
        Assert.False(note.IsCollapsed);
        string safety = Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups"), "before-migration-*.db"));
        Assert.Equal(1L, ReadScalar(safety, "PRAGMA user_version"));
        Assert.Equal("ok", ReadScalar(safety, "PRAGMA integrity_check"));
        Assert.Equal("구버전", ReadScalar(safety, "SELECT body FROM notes"));
    }

    [Fact]
    public async Task RestoresV1BackupWithoutMutatingSource()
    {
        string source = Path.Combine(directory, "old.db");
        CreateV1(source, new Note { Body = "old backup" });
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        await store.RestoreAsync(source);
        Assert.Equal("old backup", Assert.Single(await store.LoadAsync()).Body);
        using var check = new SqliteConnection($"Data Source={source};Pooling=False"); check.Open();
        using var cmd = check.CreateCommand(); cmd.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    private static void CreateV1(string path, Note note)
    {
        using var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA application_id=1296387401; PRAGMA user_version=1; CREATE TABLE notes (id TEXT PRIMARY KEY, body TEXT NOT NULL, color TEXT NOT NULL, font_size REAL NOT NULL, left_pos REAL NOT NULL, top_pos REAL NOT NULL, width REAL NOT NULL, height REAL NOT NULL, is_visible INTEGER NOT NULL, is_pinned INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, deleted_at TEXT); INSERT INTO notes VALUES ($id,$body,$color,$font,$left,$top,$width,$height,$visible,$pinned,$created,$updated,$deleted)";
        cmd.Parameters.AddWithValue("$id", note.Id.ToString()); cmd.Parameters.AddWithValue("$body", note.Body); cmd.Parameters.AddWithValue("$color", note.Color); cmd.Parameters.AddWithValue("$font", note.FontSize); cmd.Parameters.AddWithValue("$left", note.Left); cmd.Parameters.AddWithValue("$top", note.Top); cmd.Parameters.AddWithValue("$width", note.Width); cmd.Parameters.AddWithValue("$height", note.Height); cmd.Parameters.AddWithValue("$visible", note.IsVisible); cmd.Parameters.AddWithValue("$pinned", note.IsPinned); cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("O")); cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("O")); cmd.Parameters.AddWithValue("$deleted", DBNull.Value); cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task V2FontAndCollapsedStatePreserveExpandedDimensions()
    {
        var note = new Note { FontFamily = "Consolas", IsCollapsed = true, Width = 450, Height = 560 };
        using (var store = new NoteStore(Database))
        {
            await store.InitializeAsync();
            await store.SaveAsync(note);
            await store.BackupAsync(Path.Combine(directory, "v2.db"));
            await store.SaveAsync(note with { FontFamily = "Arial", IsCollapsed = false });
            await store.RestoreAsync(Path.Combine(directory, "v2.db"));
        }
        using var reopened = new NoteStore(Database);
        await reopened.InitializeAsync();
        Assert.Equal(note, Assert.Single(await reopened.LoadAsync()));
    }

    [Theory]
    [InlineData("UPDATE notes SET width=-1")]
    [InlineData("PRAGMA application_id=42")]
    public async Task RejectedV1MigrationLeavesOriginalUnchanged(string damage)
    {
        CreateV1(Database, new Note { Body = "보존" });
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = damage; cmd.ExecuteNonQuery();
        }
        byte[] original = await File.ReadAllBytesAsync(Database);
        using var store = new NoteStore(Database);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(Database));
        Assert.Equal(1L, ReadScalar(Database, "PRAGMA user_version"));
        Assert.Equal(0L, ReadScalar(Database, "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='font_family'"));
    }

    private static object? ReadScalar(string path, string sql)
    {
        using var db = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    [Fact]
    public async Task FailedReplacementRollsBackDeletion()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var note = new Note { Body = "original" };
        await store.SaveAsync(note);
        string backup = Path.Combine(directory, "rollback.db");
        await store.BackupAsync(backup);
        await store.SaveAsync(note with { Body = "must survive" });
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TRIGGER fail_insert BEFORE INSERT ON notes BEGIN SELECT RAISE(ABORT, 'simulated write failure'); END";
            cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(() => store.RestoreAsync(backup));
        Assert.Equal("must survive", Assert.Single(await store.LoadAsync()).Body);
    }

    [Fact]
    public async Task WriteFailureRejectsSaveAndRetainsPreviousContent()
    {
        using var store = new NoteStore(Database);
        await store.InitializeAsync();
        var note = new Note { Body = "saved" };
        await store.SaveAsync(note);
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_update BEFORE INSERT ON notes BEGIN SELECT RAISE(ABORT, 'simulated unavailable storage'); END";
        command.ExecuteNonQuery();
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveAsync(note with { Body = "unsaved" }));
        Assert.Equal("saved", Assert.Single(await store.LoadAsync()).Body);
    }
}

