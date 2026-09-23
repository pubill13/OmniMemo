using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Memoit.Models;

namespace Memoit.ViewModels;

/// <summary>Owns the editable snapshot. Saves never replace editor text or selection.</summary>
public sealed class NoteViewModel : INotifyPropertyChanged, IDisposable
{
    private Note note;
    private readonly Func<Note, Task> save;
    private readonly DispatcherTimer timer;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private long revision;
    private long savedRevision;
    private DateTimeOffset firstDirty, lastEdit;
    private bool disposed;
    private bool failed;
    private string status = "저장됨";
    private string saveState = "Saved";
    private static readonly Lazy<HashSet<string>> InstalledFonts = new(() =>
        System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase));

    public NoteViewModel(Note note, Func<Note, Task> save, bool isNew = false)
    {
        this.note = note;
        this.save = save;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += OnTick;
        if (isNew) Changed();
    }

    public Note Snapshot => note;
    public string Title => note.Title;
    public string Body { get => note.Body; set { if (value != note.Body) { note = note with { Body = value }; Changed(); Notify(); Notify(nameof(Title)); } } }
    public string Color { get => note.Color; set { if (value != note.Color) { note = note with { Color = value }; Changed(); Notify(); } } }
    public double FontSize { get => note.FontSize; set { if (value != note.FontSize) { note = note with { FontSize = value }; Changed(); Notify(); } } }
    public string FontFamily { get => note.FontFamily; set { if (!string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value != note.FontFamily) { note = note with { FontFamily = value }; Changed(); Notify(); Notify(nameof(EffectiveFontFamily)); } } }
    public string EffectiveFontFamily => InstalledFonts.Value.Contains(note.FontFamily) ? note.FontFamily : "Malgun Gothic";
    public bool IsCollapsed { get => note.IsCollapsed; set { if (value != note.IsCollapsed) { note = note with { IsCollapsed = value }; Changed(false); Notify(); } } }
    public bool IsPinned { get => note.IsPinned; set { if (value != note.IsPinned) { note = note with { IsPinned = value }; Changed(); Notify(); } } }
    public string SaveStatus { get => status; private set { status = value; Notify(); } }
    public string SaveState { get => saveState; private set { saveState = value; Notify(); } }
    public bool IsDirty => revision != savedRevision;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Saved;

    public void UpdateBounds(double left, double top, double width, double height)
    {
        if (IsCollapsed) { UpdatePosition(left, top); return; }
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) || width < 240 || height < 180) return;
        if (note.Left == left && note.Top == top && note.Width == width && note.Height == height) return;
        note = note with { Left = left, Top = top, Width = width, Height = height };
        Changed(false);
    }

    public void UpdatePosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || (note.Left == left && note.Top == top)) return;
        note = note with { Left = left, Top = top };
        Changed(false);
    }

    public void SetVisible(bool visible)
    {
        if (note.IsVisible == visible) return;
        note = note with { IsVisible = visible }; Changed(false);
    }

    public void SetDeleted(bool deleted)
    {
        note = note with { DeletedAt = deleted ? DateTimeOffset.UtcNow : null, IsVisible = !deleted };
        Changed();
    }

    public void SetCollapsed(bool collapsed) => IsCollapsed = collapsed;

    private void Changed(bool updateModified = true)
    {
        if (disposed) return;
        var now = DateTimeOffset.UtcNow;
        if (!IsDirty) firstDirty = now;
        lastEdit = now;
        revision++;
        if (updateModified) note = note with { UpdatedAt = now };
        SaveStatus = failed ? "저장 실패 · 변경 내용 보관 중" : "저장 중…";
        SaveState = failed ? "Failed" : "Saving";
        // A failure is retried only on a new edit or an explicit flush, never in a loop.
        failed = false;
        timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastEdit < TimeSpan.FromMilliseconds(500) && now - firstDirty < TimeSpan.FromSeconds(2)) return;
        timer.Stop();
        await FlushAsync();
    }

    public async Task<bool> FlushAsync()
    {
        await saveGate.WaitAsync();
        try
        {
            while (IsDirty && !disposed)
            {
                var snapshot = note;
                long savingRevision = revision;
                SaveState = "Saving";
                SaveStatus = "저장 중…";
                try { await save(snapshot); }
                catch (Exception ex)
                {
                    failed = true;
                    timer.Stop();
                    SaveStatus = "저장 실패 · " + ex.Message;
                    SaveState = "Failed";
                    return false;
                }
                savedRevision = savingRevision;
                failed = false;
                firstDirty = DateTimeOffset.UtcNow;
                if (!IsDirty) { SaveStatus = "저장됨"; SaveState = "Saved"; timer.Stop(); }
                Saved?.Invoke();
            }
            return !IsDirty;
        }
        finally { saveGate.Release(); }
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { disposed = true; timer.Stop(); timer.Tick -= OnTick; }
}
