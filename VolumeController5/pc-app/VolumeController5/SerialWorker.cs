using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace VolumeController5;

public enum LinkKind
{
    None = 0,
    Usb = 1,
    Bluetooth = 2
}

public sealed class SerialWorker : IDisposable
{
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;

    public event Action<float[]>? OnFrame;
    public event Action<string>? OnLog;
    public event Action? OnDisconnected;

    public bool IsOpen => _port?.IsOpen == true;
    public string? PortName => _port?.PortName;
    public int BaudRate { get; private set; }
    public LinkKind Kind { get; private set; } = LinkKind.None;

    public string[] ListPorts() => SerialPort.GetPortNames().OrderBy(x => x).ToArray();

    // Použije stejný handshake jako auto-detekce (PING -> PONG,VC5)
    public bool Probe(string portName, int baud) => TryHandshake(portName, baud);


    public bool Open(string portName, int baud, LinkKind kind)
    {
        try
        {
            Close();

            BaudRate = baud;
            Kind = kind;

            _port = new SerialPort(portName, baud)
            {
                NewLine = "\n",
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            _port.Open();

            // po otevření USB portu se Arduino může resetnout – dej mu chvíli
            Thread.Sleep(1200);

            _running = true;
            _thread = new Thread(ReadLoop) { IsBackground = true };
            _thread.Start();

            OnLog?.Invoke($"Opened {portName} @ {baud} ({kind})");
            return true;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Open failed: {ex.Message}");
            Close();
            return false;
        }
    }

    public bool AutoConnect(string? preferredUsb, string? preferredBt, bool scanFallback, out (string port, int baud, LinkKind kind) picked)
    {
        picked = default;

        if (!string.IsNullOrWhiteSpace(preferredUsb) && TryHandshake(preferredUsb, 115200))
        {
            picked = (preferredUsb, 115200, LinkKind.Usb);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(preferredBt) && TryHandshake(preferredBt, 9600))
        {
            picked = (preferredBt, 9600, LinkKind.Bluetooth);
            return true;
        }

        if (!scanFallback) return false;

        foreach (var p in ListPorts())
        {
            if (string.Equals(p, preferredBt, StringComparison.OrdinalIgnoreCase)) continue;
            if (TryHandshake(p, 115200))
            {
                picked = (p, 115200, LinkKind.Usb);
                return true;
            }
        }

        foreach (var p in ListPorts())
        {
            if (string.Equals(p, preferredUsb, StringComparison.OrdinalIgnoreCase)) continue;
            if (TryHandshake(p, 9600))
            {
                picked = (p, 9600, LinkKind.Bluetooth);
                return true;
            }
        }

        return false;
    }

    private bool TryHandshake(string portName, int baud)
    {
        try
        {
            using var sp = new SerialPort(portName, baud)
            {
                NewLine = "\n",
                ReadTimeout = 250,
                WriteTimeout = 250,
                // při SCANU vypnout DTR/RTS, aby se Arduino neresetovalo při každém pokusu
                DtrEnable = false,
                RtsEnable = false
            };

            sp.Open();
            Thread.Sleep(300);

            try { sp.DiscardInBuffer(); } catch { }
            try { sp.DiscardOutBuffer(); } catch { }

            sp.WriteLine("PING");
            Thread.Sleep(80);
            sp.WriteLine("PING");

            var deadline = DateTime.UtcNow.AddMilliseconds(2200);

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var line = sp.ReadLine().Trim();
                    if (line.StartsWith("PONG", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.IndexOf("VC5", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
                catch (TimeoutException) { }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ReadLoop()
    {
        while (_running)
        {
            try
            {
                if (_port == null || !_port.IsOpen)
                {
                    Thread.Sleep(120);
                    continue;
                }

                var line = _port.ReadLine().Trim();

                // Arduino ping (kvůli failover USB<->BT)
                if (line.Equals("PINGPC", StringComparison.OrdinalIgnoreCase))
                {
                    try { _port.WriteLine("PONGPC"); } catch { }
                    continue;
                }


                if (line.StartsWith("PONG", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryParseFrame(line, out var values))
                    OnFrame?.Invoke(values);
            }
            catch (TimeoutException) { }
            catch (IOException ex) { OnLog?.Invoke($"Disconnected: {ex.Message}"); HandleDisconnect(); return; }
            catch (InvalidOperationException ex) { OnLog?.Invoke($"Disconnected: {ex.Message}"); HandleDisconnect(); return; }
            catch (Exception ex) { OnLog?.Invoke($"Read error: {ex.Message}"); Thread.Sleep(200); }
        }
    }

    private void HandleDisconnect()
    {
        try { Close(); } catch { }
        try { OnDisconnected?.Invoke(); } catch { }
    }

    private static bool TryParseFrame(string line, out float[] values01)
    {
        values01 = new float[5];

        if (!line.StartsWith("S,", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = line.Split(',');
        if (parts.Length != 6) return false;

        for (int i = 0; i < 5; i++)
        {
            if (!int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
                return false;

            raw = Math.Clamp(raw, 0, 1023);
            values01[i] = raw / 1023f;
        }

        return true;
    }

    public void Close()
    {
        try { _running = false; } catch { }
        try { _port?.Close(); } catch { }
        try { _port?.Dispose(); } catch { }

        _port = null;
        Kind = LinkKind.None;
        BaudRate = 0;
    }

    public void Dispose() => Close();
}
