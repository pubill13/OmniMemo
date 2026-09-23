using System.Windows;
using System.Windows.Threading;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Memoit;

public partial class App : Application
{
    private readonly Dictionary<Guid, NoteViewModel> notes = [];
    private readonly Dictionary<Guid, NoteWindow> windows = [];
    private NoteStore store = null!;
    private SingleInstance? instance;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Views.MainWindow? list;
    private SettingsWindow? settings;
    private bool busy;
    private bool shuttingDown;
    private bool backupInProgress;
    private DateOnly? backupAttempt;
    private string dataDirectory = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // An explicit directory also permits isolated verification without touching personal notes.
        dataDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("OMNIMEMO_DATA_DIR")
            ?? Environment.GetEnvironmentVariable("MEMOIT_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoitPersonal"));
        try
        {
            instance = new SingleInstance(dataDirectory);
            if (!instance.IsFirst)
            {
                await instance.NotifyAsync();
                Shutdown();
                return;
            }
            string database = Path.Combine(dataDirectory, "notes.db");
            bool firstRun = !File.Exists(database);
            store = new NoteStore(database);
            await store.InitializeAsync();
            CreateList();
            CreateTray();
            if (Environment.GetEnvironmentVariable("OMNIMEMO_DATA_DIR") is null
                && Environment.GetEnvironmentVariable("MEMOIT_DATA_DIR") is null)
            {
                try { StartupRegistration.MigrateLegacyRegistration(); }
                catch (Exception ex) { Error("기존 자동 실행 등록을 갱신하지 못했습니다. 설정에서 다시 선택해 주세요.", ex); }
            }
            foreach (var note in await store.LoadAsync()) AddModel(note);
            if (firstRun && notes.Count == 0) await NewNoteAsync();
            else
            {
                foreach (var vm in notes.Values.Where(n => n.Snapshot.IsVisible && n.Snapshot.DeletedAt is null)) ShowNote(vm);
                if (windows.Count == 0) ShowList();
            }
            RefreshList();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            _ = instance.ListenAsync(() => Dispatcher.BeginInvoke(() => { if (!busy) ShowList(); }),
                ex => Dispatcher.BeginInvoke(() => Error("중복 실행 알림을 받을 수 없습니다.", ex)));
        }
        catch (Exception ex)
        {
            Error("프로그램을 시작할 수 없습니다. 기존 데이터는 자동으로 초기화하지 않습니다.\n데이터 위치: " + dataDirectory, ex);
            Shutdown(1);
        }
    }

    private void CreateList()
    {
        list = new Views.MainWindow();
        MainWindow = list;
        list.NewNoteRequested += () => RunOperation(NewNoteAsync);
        list.OpenNoteRequested += id => RunOperation(async () =>
        {
            if (notes.TryGetValue(id, out var vm) && vm.Snapshot.DeletedAt is null)
            { vm.SetVisible(true); ShowNote(vm); await vm.FlushAsync(); }
        });
        list.RestoreNoteRequested += id => RunOperation(async () =>
        {
            var vm = notes[id];
            vm.SetDeleted(false);
            if (await vm.FlushAsync()) ShowNote(vm);
            else { vm.SetDeleted(true); throw new IOException("메모를 복원하지 못했습니다. 다시 시도하세요."); }
        });
        list.PermanentDeleteRequested += id => RunOperation(async () =>
        {
            if (MessageBox.Show(list, "이 메모를 영구 삭제할까요? 휴지통에서 복구할 수 없습니다.", "영구 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var vm = notes[id];
            if (!await vm.FlushAsync()) throw new IOException(vm.SaveStatus);
            await store.DeletePermanentlyAsync(id);
            if (windows.Remove(id, out var deletedWindow)) { deletedWindow.AllowClose = true; deletedWindow.Close(); }
            vm.Dispose(); notes.Remove(id);
        });
        list.SettingsRequested += () => { if (!busy) ShowSettings(); };
    }

    private void CreateTray()
    {
        using (var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/OmniMemo.ico")).Stream)
        using (var icon = new System.Drawing.Icon(iconStream))
            trayIcon = (System.Drawing.Icon)icon.Clone();
        tray = new Forms.NotifyIcon { Text = "OmniMemo · 개인용 메모", Icon = trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("새 메모", null, (_, _) => RunOperation(NewNoteAsync));
        menu.Items.Add("메모 목록", null, (_, _) => { if (!busy) ShowList(); });
        menu.Items.Add("전체 숨기기", null, (_, _) => RunOperation(() => SetAllVisibleAsync(false)));
        menu.Items.Add("전체 보이기", null, (_, _) => RunOperation(() => SetAllVisibleAsync(true)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("설정", null, (_, _) => { if (!busy) ShowSettings(); });
        menu.Items.Add("종료", null, (_, _) => RunOperation(ExitAsync));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => { if (!busy) ShowList(); };
        tray.BalloonTipClicked += (_, _) => { if (!busy) ShowSettings(); };
    }

    private NoteViewModel AddModel(Note note, bool isNew = false)
    {
        var vm = new NoteViewModel(note, store.SaveAsync, isNew);
        vm.Saved += OnNoteSaved;
        notes.Add(note.Id, vm);
        return vm;
    }

    private async Task NewNoteAsync()
    {
        int offset = windows.Count % 10 * 24;
        var note = new Note { Left = 100 + offset, Top = 100 + offset };
        var vm = AddModel(note, true);
        ShowNote(vm);
        if (!await vm.FlushAsync()) Error("새 메모를 저장하지 못했습니다. 창의 내용을 복사해 보관하거나 저장을 다시 시도하세요.");
    }

    private void ShowNote(NoteViewModel vm)
    {
        if (!windows.TryGetValue(vm.Snapshot.Id, out var window))
        {
            window = new NoteWindow(vm);
            var target = window;
            window.NewNoteRequested += () => RunOperation(NewNoteAsync);
            window.SearchRequested += () => { if (!busy) ShowList(true); };
            window.HideRequested += (_, _) => RunOperation(async () =>
            {
                vm.SetVisible(false);
                if (await vm.FlushAsync()) target.Hide();
                else { vm.SetVisible(true); Error("저장에 실패해 메모를 숨기지 않았습니다."); }
            });
            window.DeleteRequested += () => RunOperation(async () =>
            {
                vm.SetDeleted(true);
                if (await vm.FlushAsync()) target.Hide();
                else { vm.SetDeleted(false); Error("저장에 실패해 메모를 삭제하지 않았습니다."); }
            });
            window.SourceInitialized += (_, _) => WindowPlacement.KeepOnScreen(target);
            window.Loaded += (_, _) => WindowPlacement.KeepOnScreen(target);
            window.SourceInitialized += (_, _) =>
            {
                var magnet = new WindowMagnet(target, () => windows.Where(pair => pair.Value != target
                    && notes.TryGetValue(pair.Key, out var peer) && peer.Snapshot.DeletedAt is null)
                    .Select(pair => (Window)pair.Value));
                target.TileMoveRequested += magnet.MoveTo;
                target.Closed += (_, _) => magnet.Dispose();
            };
            windows.Add(vm.Snapshot.Id, window);
        }
        window.Show();
        WindowPlacement.KeepOnScreen(window);
        window.Activate();
    }

    private void ShowList(bool search = false)
    {
        if (list is null) return;
        RefreshList(); list.Show(); list.WindowState = WindowState.Normal;
        WindowPlacement.KeepOnScreen(list); list.Activate();
        if (search) list.FocusSearch();
    }

    private void RefreshList() => list?.SetNotes(notes.Values.Select(n => n.Snapshot), list.ShowingTrash);

    private async void OnNoteSaved()
    {
        RefreshList();
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (backupInProgress || backupAttempt == today) return;
        backupAttempt = today;
        backupInProgress = true;
        try { await store.CreateDailyBackupAsync(Path.Combine(dataDirectory, "backups")); }
        catch (Exception ex)
        {
            tray?.ShowBalloonTip(8000, "자동 백업 실패", "메모는 저장됐지만 백업하지 못했습니다. 설정에서 수동 백업을 해 주세요. " + ex.Message, Forms.ToolTipIcon.Warning);
        }
        finally { backupInProgress = false; }
    }

    private async Task SetAllVisibleAsync(bool visible)
    {
        foreach (var vm in notes.Values.Where(n => n.Snapshot.DeletedAt is null))
        {
            vm.SetVisible(visible);
            if (!await vm.FlushAsync()) { vm.SetVisible(true); ShowNote(vm); throw new IOException(vm.SaveStatus); }
            if (visible) ShowNote(vm);
            else if (windows.TryGetValue(vm.Snapshot.Id, out var window)) window.Hide();
        }
    }

    private void ShowSettings()
    {
        try
        {
            if (settings is not null) { settings.Activate(); return; }
            settings = new SettingsWindow(StartupRegistration.IsEnabled(), dataDirectory);
            settings.Closed += (_, _) => settings = null;
            settings.AutoStartChanged += enabled =>
            {
                try { StartupRegistration.SetEnabled(enabled); }
                catch (Exception ex) { settings?.SetAutoStart(!enabled); Error("자동 실행 설정을 변경하지 못했습니다.", ex); }
            };
            settings.BackupRequested += () => RunOperation(BackupAsync);
            settings.RestoreRequested += () => RunOperation(RestoreAsync);
            settings.Show();
        }
        catch (Exception ex) { Error("설정을 열지 못했습니다.", ex); }
    }

    private async Task BackupAsync()
    {
        var dialog = new SaveFileDialog { Title = "메모 전체 백업", Filter = "OmniMemo 백업 (*.db)|*.db", FileName = $"OmniMemo-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db" };
        if (dialog.ShowDialog(settings) != true) return;
        if (!await FlushAllAsync()) throw new IOException("저장하지 못한 변경이 있어 백업을 중단했습니다.");
        await store.BackupAsync(dialog.FileName);
        Dialogs.Show("백업을 저장했습니다.\n" + dialog.FileName, "메모 전체 백업", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RestoreAsync()
    {
        var dialog = new OpenFileDialog { Title = "메모 백업 복원", Filter = "OmniMemo 백업 (*.db)|*.db", CheckFileExists = true };
        if (dialog.ShowDialog(settings) != true) return;
        if (Dialogs.Show("현재 메모 전체를 선택한 백업으로 교체합니다. 현재 데이터는 안전 백업으로 보관합니다.\n계속할까요?", "백업 복원", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!await FlushAllAsync()) throw new IOException("저장하지 못한 변경이 있어 복원을 중단했습니다.");
        await store.RestoreAsync(dialog.FileName);
        CloseNoteWindows();
        foreach (var vm in notes.Values) vm.Dispose();
        notes.Clear();
        foreach (var note in await store.LoadAsync()) AddModel(note);
        foreach (var vm in notes.Values.Where(n => n.Snapshot.IsVisible && n.Snapshot.DeletedAt is null)) ShowNote(vm);
        RefreshList();
        Dialogs.Show("백업을 복원했습니다. 복원 전 데이터는 데이터 폴더의 backups에 보관했습니다.", "복원 완료");
    }

    private async Task<bool> FlushAllAsync()
    {
        bool success = true;
        foreach (var vm in notes.Values) success &= await vm.FlushAsync();
        return success;
    }

    private async Task ExitAsync()
    {
        if (!await FlushAllAsync()) { ShowList(); throw new IOException("저장에 실패해 종료를 취소했습니다. 메모 창의 내용을 보관한 뒤 다시 시도하세요."); }
        shuttingDown = true;
        CloseNoteWindows();
        if (list is not null) { list.AllowClose = true; list.Close(); }
        settings?.Close();
        Shutdown();
    }

    private void CloseNoteWindows()
    {
        foreach (var window in windows.Values) { window.AllowClose = true; window.Close(); }
        windows.Clear();
    }

    private async void RunOperation(Func<Task> action)
    {
        if (busy || shuttingDown) return;
        busy = true;
        foreach (Window window in Windows) window.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { Error("작업을 완료하지 못했습니다.", ex); }
        finally
        {
            busy = false;
            if (!shuttingDown)
            {
                foreach (Window window in Windows) window.IsEnabled = true;
                RefreshList();
            }
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        foreach (Window window in Windows) if (window.IsVisible) WindowPlacement.KeepOnScreen(window);
    });

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows gives no asynchronous save contract. Cancel once rather than lose pending input.
        if (busy || notes.Values.Any(n => n.IsDirty))
        {
            e.Cancel = true;
            RunOperation(ExitAsync);
        }
        base.OnSessionEnding(e);
    }

    private static void Error(string message, Exception? ex = null) => Dialogs.Show(message + (ex is null ? "" : "\n\n" + ex.Message), "OmniMemo", MessageBoxButton.OK, MessageBoxImage.Error);

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        tray?.Dispose();
        trayIcon?.Dispose();
        foreach (var vm in notes.Values) vm.Dispose();
        store?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}


