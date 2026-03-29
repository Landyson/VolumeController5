using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace VolumeController5;

public partial class PinnedAppsWindow : Window
{
    private readonly ObservableCollection<string> _running = new();
    private readonly ObservableCollection<string> _pinned = new();

    private List<string> _runningAll = new();

    public IReadOnlyList<string> ResultPinned => _pinned.ToList();

    public PinnedAppsWindow(IEnumerable<string> initialPinned)
    {
        InitializeComponent();

        foreach (var x in NormalizeMany(initialPinned))
            _pinned.Add(x);

        PinnedList.ItemsSource = _pinned;
        RunningList.ItemsSource = _running;

        Loaded += async (_, __) =>
        {
            await LoadRunningAsync();
            RefreshRunningFilter();
            StatusText.Text = $"Běžící: {_runningAll.Count} | Připnuté: {_pinned.Count}";
        };

        SearchBox.TextChanged += (_, __) => RefreshRunningFilter();

        AddBtn.Click += (_, __) => AddSelectedRunning();
        RemoveBtn.Click += (_, __) => RemoveSelectedPinned();

        AddManualBtn.Click += (_, __) =>
        {
            var exe = NormalizeExe(ManualExeBox.Text);
            if (exe == null) return;

            AddPinned(exe);
            ManualExeBox.Text = "";
        };

        ClearAllBtn.Click += (_, __) =>
        {
            if (_pinned.Count == 0) return;

            if (System.Windows.MessageBox.Show("Odebrat všechny připnuté aplikace?", "Potvrzení",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
                return;

            _pinned.Clear();
            RefreshRunningFilter();
        };

        OkBtn.Click += (_, __) =>
        {
            DialogResult = true;
            Close();
        };

        CancelBtn.Click += (_, __) =>
        {
            DialogResult = false;
            Close();
        };

        // dvojklik = přidat/odebrat
        RunningList.MouseDoubleClick += (_, __) => AddSelectedRunning();
        PinnedList.MouseDoubleClick += (_, __) => RemoveSelectedPinned();
    }

    private async Task LoadRunningAsync()
    {
        StatusText.Text = "Načítám běžící aplikace…";

        _runningAll = await Task.Run(() =>
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(p.ProcessName)) continue;
                    set.Add(p.ProcessName + ".exe");
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    private void RefreshRunningFilter()
    {
        var q = (SearchBox.Text ?? "").Trim();

        IEnumerable<string> list = _runningAll;

        if (!string.IsNullOrWhiteSpace(q))
        {
            list = list.Where(x => x.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // ať není zbytečně dlouhý seznam: neukazuj, co už je připnuté
        var pinnedSet = new HashSet<string>(_pinned, StringComparer.OrdinalIgnoreCase);
        list = list.Where(x => !pinnedSet.Contains(x));

        _running.Clear();
        foreach (var x in list) _running.Add(x);

        StatusText.Text = $"Běžící: {_runningAll.Count} | Zobrazeno: {_running.Count} | Připnuté: {_pinned.Count}";
    }

    private void AddSelectedRunning()
    {
        if (RunningList.SelectedItem is not string exe) return;
        AddPinned(exe);
    }

    private void RemoveSelectedPinned()
    {
        if (PinnedList.SelectedItem is not string exe) return;
        _pinned.Remove(exe);
        RefreshRunningFilter();
    }

    private void AddPinned(string exe)
    {
        if (_pinned.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase))) return;
        _pinned.Add(exe);

        // udrž pořádek
        var sorted = _pinned.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        _pinned.Clear();
        foreach (var x in sorted) _pinned.Add(x);

        RefreshRunningFilter();
    }

    private static IEnumerable<string> NormalizeMany(IEnumerable<string> items)
        => items.Select(NormalizeExe).Where(x => x != null)!.Cast<string>();

    private static string? NormalizeExe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var t = text.Trim().Trim('"');

        // když někdo vloží cestu, vezmi jen název souboru
        try
        {
            if (t.Contains("\\") || t.Contains("/"))
                t = Path.GetFileName(t);
        }
        catch { }

        if (string.IsNullOrWhiteSpace(t)) return null;

        if (!t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            t += ".exe";

        return t;
    }
}
