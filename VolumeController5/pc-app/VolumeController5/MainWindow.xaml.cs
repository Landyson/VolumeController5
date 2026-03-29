using System;
using System.ComponentModel;
using System.IO;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfSlider = System.Windows.Controls.Slider;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using System.Windows.Threading;

namespace VolumeController5;

public partial class MainWindow : Window
{
    private readonly SerialWorker _serial = new();
    private readonly object _serialLock = new();

    private AppConfig _cfg = AppConfig.Load();

    private readonly WpfComboBox[] _targets;
    private readonly WpfSlider[] _sliders;
    private readonly WpfTextBlock[] _values;

    private readonly object _valsLock = new();
    private readonly float[] _latest = new float[5] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
    private bool _uiNeedsUpdate;
    private bool _audioDirty;
    private bool _ignoreUi;

    private readonly DispatcherTimer _uiTimer = new();
    private readonly DispatcherTimer _sessionsTimer = new();
    private DateTime _lastSessionsRefresh = DateTime.MinValue;

    private readonly AutoResetEvent _applyEvent = new(false);
    private readonly object _applyLock = new();
    private bool _applyPending;
    private bool _closing;
    private float? _queuedMaster;
    private Dictionary<string, float> _queuedExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _applyThread;

    private CancellationTokenSource? _connCts;
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowExit;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTrayIcon();

        _targets = new[] { Target0, Target1, Target2, Target3, Target4 };
        _sliders = new[] { Slider0, Slider1, Slider2, Slider3, Slider4 };
        _values = new[] { Value0, Value1, Value2, Value3, Value4 };

        ReconnectBtn.Click += async (_, __) => await RestartConnectionAsync();

        ManageAppsBtn.Click += async (_, __) =>
        {
            var dlg = new PinnedAppsWindow(_cfg.PinnedExe) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            _cfg.PinnedExe = dlg.ResultPinned
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cfg.Save();
            UpdatePinnedInfo();

            await RefreshTargetsAsync(force: true);
            MarkAudioDirty();
            Log("Připnuté aplikace aktualizovány.");
        };

        PortSettingsBtn.Click += async (_, __) =>
        {
            var dlg = new PortSettingsWindow(_cfg.PreferredUsbPort, _cfg.PreferredBtPort) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            _cfg.PreferredBtPort = dlg.SelectedBtPort;
            _cfg.Save();

            Log($"Nastaven BT port: {(_cfg.PreferredBtPort ?? "(auto)")}");
            await RestartConnectionAsync();
        };

        foreach (var (slider, idx) in _sliders.Select((s, i) => (s, i)))
        {
            slider.ValueChanged += (_, __) =>
            {
                if (_ignoreUi) return;

                _values[idx].Text = slider.Value.ToString("0.00");

                lock (_valsLock)
                {
                    _latest[idx] = (float)slider.Value;
                    _audioDirty = true;
                }
            };
        }

        foreach (var cb in _targets)
        {
            cb.SelectionChanged += (_, __) =>
            {
                SaveConfigFromUi();
                _cfg.Save();
                MarkAudioDirty();
            };
        }

        _serial.OnLog += Log;
        _serial.OnFrame += values =>
        {
            lock (_valsLock)
            {
                bool changed = false;
                for (int i = 0; i < 5; i++)
                {
                    if (Math.Abs(_latest[i] - values[i]) > 0.004f) changed = true;
                    _latest[i] = values[i];
                }

                if (changed)
                {
                    _uiNeedsUpdate = true;
                    _audioDirty = true;
                }
            }
        };

        _serial.OnDisconnected += () =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus();
                Log("Port odpojen → connection manager se postará o znovu připojení.");
            });
        };

        _applyThread = new Thread(ApplyLoop)
        {
            IsBackground = true,
            Name = "VolumeApplyWorker"
        };
        try { _applyThread.SetApartmentState(ApartmentState.MTA); } catch { }
        _applyThread.Start();

        _uiTimer.Interval = TimeSpan.FromMilliseconds(50);
        _uiTimer.Tick += (_, __) => UiTick();

        _sessionsTimer.Interval = TimeSpan.FromSeconds(4);
        _sessionsTimer.Tick += async (_, __) => await RefreshTargetsAsync(force: false);

        Loaded += async (_, __) =>
        {
            UpdatePinnedInfo();
            await RefreshTargetsAsync(force: true);
            LoadConfigToUi();
            await RestartConnectionAsync();

            _uiTimer.Start();
            _sessionsTimer.Start();
        };

        Closing += MainWindow_Closing;
        Closed += (_, __) =>
        {
            _closing = true;
            _uiTimer.Stop();
            _sessionsTimer.Stop();

            try { _connCts?.Cancel(); } catch { }

            SaveConfigFromUi();
            _cfg.Save();

            lock (_serialLock) { _serial.Dispose(); }
            _applyEvent.Set();
        };
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon();

        try
        {
            var streamInfo = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"));

            if (streamInfo?.Stream != null)
                _trayIcon.Icon = new Drawing.Icon(streamInfo.Stream);
            else
                _trayIcon.Icon = Drawing.SystemIcons.Application;
        }
        catch
        {
            _trayIcon.Icon = Drawing.SystemIcons.Application;
        }

        _trayIcon.Text = "VolumeController5";
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, __) => Dispatcher.Invoke(ShowFromTray);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Otevřít", null, (_, __) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Konec", null, (_, __) => Dispatcher.Invoke(ExitFromTray));
        _trayIcon.ContextMenuStrip = menu;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit) return;

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();

        if (_trayIcon != null && !_trayHintShown)
        {
            _trayHintShown = true;
            try
            {
                _trayIcon.ShowBalloonTip(2500, "VolumeController5", "Aplikace běží dál na pozadí. Otevřeš ji z ikony v oznamovací oblasti.", Forms.ToolTipIcon.Info);
            }
            catch { }
        }
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitFromTray()
    {
        _allowExit = true;
        Close();
    }

    private void UpdatePinnedInfo()
    {
        var list = _cfg.PinnedExe
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PinnedInfoText.Text = list.Count == 0
            ? "Připnuté: (žádné)"
            : "Připnuté: " + string.Join(", ", list);
    }

    private void MarkAudioDirty()
    {
        lock (_valsLock) { _audioDirty = true; }
    }

    private void UiTick()
    {
        float[] snap = new float[5];
        bool doUi;
        bool doAudio;

        lock (_valsLock)
        {
            doUi = _uiNeedsUpdate;
            doAudio = _audioDirty;

            for (int i = 0; i < 5; i++) snap[i] = _latest[i];

            _uiNeedsUpdate = false;
            _audioDirty = false;
        }

        if (doUi)
        {
            _ignoreUi = true;
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    if (_sliders[i].IsMouseCaptureWithin) continue;
                    _sliders[i].Value = snap[i];
                    _values[i].Text = snap[i].ToString("0.00");
                }
            }
            finally
            {
                _ignoreUi = false;
            }
        }

        if (doAudio)
            QueueApplyFromSnapshot(snap);
    }

    private void QueueApplyFromSnapshot(float[] snap)
    {
        float? master = null;
        var exe = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 5; i++)
        {
            var target = _targets[i].SelectedItem as string ?? "MASTER";
            var vol = snap[i];

            if (string.Equals(target, "MASTER", StringComparison.OrdinalIgnoreCase))
            {
                master = vol;
            }
            else
            {
                exe[target] = vol;
            }
        }

        lock (_applyLock)
        {
            _queuedMaster = master;
            _queuedExe = exe;

            if (!_applyPending)
            {
                _applyPending = true;
                _applyEvent.Set();
            }
        }
    }

    private void ApplyLoop()
    {
        while (!_closing)
        {
            _applyEvent.WaitOne();

            while (true)
            {
                float? master;
                Dictionary<string, float> exe;

                lock (_applyLock)
                {
                    if (!_applyPending) break;

                    master = _queuedMaster;
                    exe = _queuedExe;
                    _applyPending = false;
                }

                try { AudioController.ApplyVolumes(master, exe); }
                catch { }
            }
        }
    }

    private async Task RefreshTargetsAsync(bool force)
    {
        if (!force)
        {
            if (_targets.Any(cb => cb.IsDropDownOpen)) return;
            if ((DateTime.UtcNow - _lastSessionsRefresh) < TimeSpan.FromSeconds(4)) return;
        }

        _lastSessionsRefresh = DateTime.UtcNow;

        var pinned = _cfg.PinnedExe
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ensure = _cfg.SlotTargets
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "MASTER", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = await Task.Run(() =>
        {
            var sessionExe = AudioController.ListSessions()
                .Select(s => s.ProcessName)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "unknown.exe", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.UnionWith(pinned);
            set.UnionWith(ensure);
            set.UnionWith(sessionExe);

            var list = new List<string> { "MASTER" };

            foreach (var x in pinned.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                list.Add(x);

            foreach (var x in ensure.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                if (!list.Contains(x, StringComparer.OrdinalIgnoreCase))
                    list.Add(x);

            foreach (var x in set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                if (!list.Contains(x, StringComparer.OrdinalIgnoreCase))
                    list.Add(x);

            return list;
        });

        var selectedBefore = _targets.Select(cb => cb.SelectedItem as string).ToArray();

        foreach (var (cb, idx) in _targets.Select((cb, i) => (cb, i)))
        {
            var current = cb.ItemsSource as IEnumerable<string>;
            if (current != null && current.SequenceEqual(items)) continue;

            cb.ItemsSource = items;

            var sel = selectedBefore[idx];
            if (!string.IsNullOrWhiteSpace(sel) && items.Contains(sel, StringComparer.OrdinalIgnoreCase))
                cb.SelectedItem = items.First(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            else
                cb.SelectedIndex = 0;
        }

        MarkAudioDirty();

        if (force)
            Log($"Seznam: {items.Count - 1} EXE + MASTER (připnuté + sessions)");
    }

    private void LoadConfigToUi()
    {
        for (int i = 0; i < 5; i++)
        {
            var t = _cfg.SlotTargets.ElementAtOrDefault(i) ?? "MASTER";
            _targets[i].SelectedItem = t;
        }
    }

    private void SaveConfigFromUi()
    {
        for (int i = 0; i < 5; i++)
            _cfg.SlotTargets[i] = (_targets[i].SelectedItem as string) ?? "MASTER";
    }

    private void Log(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            LogBox.ScrollToEnd();
        });
    }

    private void UpdateStatus()
    {
        lock (_serialLock)
        {
            if (_serial.IsOpen)
            {
                StatusText.Text = "Připojeno";
                PortText.Text = $"{_serial.PortName} ({_serial.Kind}, {_serial.BaudRate})";
            }
            else
            {
                StatusText.Text = "Odpojeno";
                PortText.Text = "(hledám…)";
            }
        }
    }

    private string? FindUsbCandidate_NoOpen()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.PreferredUsbPort) && _serial.Probe(_cfg.PreferredUsbPort, 115200))
            return _cfg.PreferredUsbPort;

        var ports = _serial.ListPorts();
        foreach (var p in ports)
        {
            if (!string.IsNullOrWhiteSpace(_cfg.PreferredBtPort) &&
                string.Equals(p, _cfg.PreferredBtPort, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_serial.Probe(p, 115200))
                return p;
        }

        return null;
    }

    private string? FindBtCandidate_NoOpen()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.PreferredBtPort))
        {
            if (_serial.Probe(_cfg.PreferredBtPort, 9600))
                return _cfg.PreferredBtPort;

            return null;
        }

        var ports = _serial.ListPorts();
        foreach (var p in ports)
        {
            if (!string.IsNullOrWhiteSpace(_cfg.PreferredUsbPort) &&
                string.Equals(p, _cfg.PreferredUsbPort, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_serial.Probe(p, 9600))
                return p;
        }

        return null;
    }

    private async Task RestartConnectionAsync()
    {
        try { _connCts?.Cancel(); } catch { }

        lock (_serialLock) { _serial.Close(); }
        UpdateStatus();

        _connCts = new CancellationTokenSource();
        _ = ConnectionLoopAsync(_connCts.Token);

        await Task.CompletedTask;
    }

    private async Task ConnectionLoopAsync(CancellationToken ct)
    {
        int failDelayMs = 1500;
        int btToUsbProbeCounter = 0;

        while (!ct.IsCancellationRequested && !_closing)
        {
            bool isOpen;
            LinkKind kind;

            lock (_serialLock)
            {
                isOpen = _serial.IsOpen;
                kind = _serial.Kind;
            }

            if (isOpen)
            {
                if (kind == LinkKind.Bluetooth)
                {
                    btToUsbProbeCounter++;
                    if (btToUsbProbeCounter >= 2)
                    {
                        btToUsbProbeCounter = 0;

                        string? usb;
                        lock (_serialLock)
                        {
                            usb = FindUsbCandidate_NoOpen();
                        }

                        if (!string.IsNullOrWhiteSpace(usb))
                        {
                            Log($"Nalezen USB ({usb}) → přepínám z BT na USB…");

                            lock (_serialLock)
                            {
                                _serial.Close();
                                if (_serial.Open(usb, 115200, LinkKind.Usb))
                                    SavePicked(usb, LinkKind.Usb);
                            }

                            Dispatcher.Invoke(UpdateStatus);
                            Log("Přepnuto na USB.");
                        }
                    }
                }

                await Task.Delay(2000, ct);
                continue;
            }

            bool connected = await Task.Run(() =>
            {
                lock (_serialLock)
                {
                    var usb = FindUsbCandidate_NoOpen();
                    if (!string.IsNullOrWhiteSpace(usb))
                    {
                        if (_serial.Open(usb, 115200, LinkKind.Usb))
                        {
                            SavePicked(usb, LinkKind.Usb);
                            return true;
                        }
                    }

                    var bt = FindBtCandidate_NoOpen();
                    if (!string.IsNullOrWhiteSpace(bt))
                    {
                        if (_serial.Open(bt, 9600, LinkKind.Bluetooth))
                        {
                            SavePicked(bt, LinkKind.Bluetooth);
                            return true;
                        }
                    }

                    return false;
                }
            }, ct);

            if (connected)
            {
                failDelayMs = 1500;
                Dispatcher.Invoke(UpdateStatus);
                Log("Auto připojeno.");
            }
            else
            {
                await Task.Delay(failDelayMs, ct);
                failDelayMs = Math.Min(10000, (int)(failDelayMs * 1.4));
            }
        }
    }

    private void SavePicked(string port, LinkKind kind)
    {
        if (kind == LinkKind.Usb) _cfg.PreferredUsbPort = port;
        if (kind == LinkKind.Bluetooth) _cfg.PreferredBtPort = port;
        _cfg.Save();
    }
}
