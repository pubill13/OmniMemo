using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Memoit.Models;
using ICSharpCode.AvalonEdit.Document;

namespace Memoit.ViewModels;

/// <summary>Owns the editable snapshot. Saves never replace editor text or selection.</summary>
public sealed class NoteViewModel : INotifyPropertyChanged, IDisposable
{
    // Keep small saves synchronous; large UTF-16 copies should not occupy the dispatcher.
    private const int BackgroundTextThreshold = 65_536;
    private Note note;
    private string title = "";
    private bool bodyChanged;
    private int titleEnd;
    public TextDocument Document { get; }
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

    public NoteViewModel(Note note, Func<Note, Task> save, bool isNew = false)
    {
        this.note = note;
        Document = new TextDocument(note.Body);
        UpdateTitle();
        Document.Changed += OnDocumentChanged;
        this.save = save;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += OnTick;
        if (isNew) Changed();
    }

    public Note Snapshot
    {
        get
        {
            if (bodyChanged) { note = note with { Body = Document.Text }; bodyChanged = false; }
            return note;
        }
    }
    public string Title => title;
    public string Body
    {
        get => Snapshot.Body;
        set
        {
            if (value == Body) return;
            Document.Text = value;
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        bodyChanged = true;
        string previousTitle = title;
        if (e.Offset <= titleEnd) UpdateTitle();
        Changed(); Notify(nameof(Body));
        if (title != previousTitle) Notify(nameof(Title));
    }

    private void UpdateTitle()
    {
        foreach (var line in Document.Lines)
        {
            int start = line.Offset, end = line.EndOffset;
            while (start < end && char.IsWhiteSpace(Document.GetCharAt(start))) start++;
            if (start == end) continue;
            while (end > start && char.IsWhiteSpace(Document.GetCharAt(end - 1))) end--;
            title = Document.GetText(start, Math.Min(80, end - start));
            titleEnd = line.EndOffset;
            return;
        }
        title = "새 메모";
        titleEnd = Document.TextLength;
    }
    public string Color { get => note.Color; set { if (value != note.Color) { note = note with { Color = value }; Changed(); Notify(); } } }
    public double FontSize { get => note.FontSize; set { if (value != note.FontSize) { note = note with { FontSize = value }; Changed(); Notify(); } } }
    public string FontFamily { get => note.FontFamily; set { if (!string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value != note.FontFamily) { note = note with { FontFamily = value }; Changed(); Notify(); Notify(nameof(EffectiveFontFamily)); Notify(nameof(ResolvedFontFamily)); } } }
    public System.Windows.Media.FontFamily ResolvedFontFamily => Memoit.Services.NoteFonts.Create(note.FontFamily);
    public string EffectiveFontFamily => Memoit.Services.NoteFonts.Resolve(note.FontFamily);
    public bool IsCollapsed
    {
        get => note.IsCollapsed;
        set
        {
            if (value == note.IsCollapsed) return;
            note = note.IsCollapsed
                ? note with { CollapsedLeft = note.Left, CollapsedTop = note.Top }
                : note with { ExpandedLeft = note.Left, ExpandedTop = note.Top };
            note = note with { IsCollapsed = value,
                Left = value ? note.CollapsedLeft ?? note.Left : note.Left,
                Top = value ? note.CollapsedTop ?? note.Top : note.Top };
            Changed(false); Notify();
        }
    }
    public bool IsPinned { get => note.IsPinned; set { if (value != note.IsPinned) { note = note with { IsPinned = value }; Changed(); Notify(); } } }
    public string SaveStatus { get => status; private set { if (status == value) return; status = value; Notify(); } }
    public string SaveState { get => saveState; private set { if (saveState == value) return; saveState = value; Notify(); } }
    public bool IsDirty => revision != savedRevision;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Saved;

    public void UpdateBounds(double left, double top, double width, double height)
    {
        if (IsCollapsed) { UpdatePosition(left, top); return; }
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) || width < 36 || height < 36) return;
        if (note.Left == left && note.Top == top && note.Width == width && note.Height == height) return;
        note = note with { Left = left, Top = top, Width = width, Height = height, ExpandedLeft = left, ExpandedTop = top };
        Changed(false);
    }

    public void UpdatePosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || (note.Left == left && note.Top == top)) return;
        note = note.IsCollapsed
            ? note with { Left = left, Top = top, CollapsedLeft = left, CollapsedTop = top }
            : note with { Left = left, Top = top, ExpandedLeft = left, ExpandedTop = top };
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

    public void RestoreLifecycle(Note state, bool preserveFailure = false)
    {
        string failure = SaveStatus;
        bool collapseChanged = note.IsCollapsed != state.IsCollapsed;
        note = note with { DeletedAt = state.DeletedAt, IsVisible = state.IsVisible,
            IsCollapsed = state.IsCollapsed, Left = state.Left, Top = state.Top,
            Width = state.Width, Height = state.Height, CollapsedLeft = state.CollapsedLeft,
            CollapsedTop = state.CollapsedTop, ExpandedLeft = state.ExpandedLeft, ExpandedTop = state.ExpandedTop };
        Changed(false);
        if (collapseChanged) Notify(nameof(IsCollapsed));
        if (preserveFailure)
        {
            failed = true; timer.Stop(); SaveState = "Failed"; SaveStatus = failure;
        }
    }

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
                // Capture an immutable document revision before yielding. Flattening large text
                // belongs off the dispatcher; later edits must not change the saved revision.
                var metadata = note;
                var text = Document.CreateSnapshot();
                long savingRevision = revision;
                SaveState = "Saving";
                SaveStatus = "저장 중…";
                try
                {
                    var snapshot = bodyChanged && text.TextLength >= BackgroundTextThreshold
                        ? await Task.Run(() => metadata with { Body = text.Text })
                        : bodyChanged ? metadata with { Body = text.Text } : metadata;
                    await save(snapshot);
                    if (revision == savingRevision)
                    {
                        note = note with { Body = snapshot.Body };
                        bodyChanged = false;
                    }
                }
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

    /// <summary>Called on the UI dispatcher. Gates serialize batches with each editor's autosave.</summary>
    public static async Task<bool> FlushManyAsync(IEnumerable<NoteViewModel> source, Func<IReadOnlyList<Note>, Task> saveMany)
    {
        var models = source.Distinct().OrderBy(vm => vm.note.Id).ToArray();
        var acquired = new List<NoteViewModel>();
        try
        {
            foreach (var vm in models) { await vm.saveGate.WaitAsync(); acquired.Add(vm); }
            var pending = models.Where(vm => vm.IsDirty && !vm.disposed)
                .Select(vm => (Vm: vm, Metadata: vm.note, Text: vm.Document.CreateSnapshot(),
                    BodyChanged: vm.bodyChanged, Revision: vm.revision)).ToArray();
            if (pending.Length == 0) return true;
            foreach (var item in pending) { item.Vm.timer.Stop(); item.Vm.SaveState = "Saving"; item.Vm.SaveStatus = "저장 중…"; }
            Note[] snapshots;
            try
            {
                snapshots = await Task.Run(() => pending.Select(item => item.BodyChanged
                    ? item.Metadata with { Body = item.Text.Text } : item.Metadata).ToArray());
                await saveMany(snapshots);
            }
            catch (Exception ex)
            {
                foreach (var item in pending)
                {
                    item.Vm.failed = true; item.Vm.timer.Stop();
                    item.Vm.SaveState = "Failed"; item.Vm.SaveStatus = "저장 실패 · " + ex.Message;
                }
                return false;
            }
            for (int i = 0; i < pending.Length; i++)
            {
                var item = pending[i];
                var vm = item.Vm;
                vm.savedRevision = item.Revision;
                vm.failed = false;
                vm.firstDirty = DateTimeOffset.UtcNow;
                if (!vm.IsDirty)
                {
                    vm.note = vm.note with { Body = snapshots[i].Body };
                    vm.bodyChanged = false;
                    vm.SaveState = "Saved"; vm.SaveStatus = "저장됨"; vm.timer.Stop();
                }
                else if (!vm.disposed) vm.timer.Start();
                vm.Saved?.Invoke();
            }
            return true;
        }
        finally { foreach (var vm in acquired) vm.saveGate.Release(); }
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { disposed = true; timer.Stop(); timer.Tick -= OnTick; Document.Changed -= OnDocumentChanged; }
}
