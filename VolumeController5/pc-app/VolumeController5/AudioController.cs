using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NAudio.CoreAudioApi;

namespace VolumeController5;

public sealed class AudioSessionInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public static class AudioController
{
    private static MMDevice GetDefaultDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    // PID může být v NAudio jako property GetProcessID (ano property)
    private static int TryGetProcessId(AudioSessionControl session)
    {
        var t = session.GetType();

        var propGetPid = t.GetProperty("GetProcessID", BindingFlags.Instance | BindingFlags.Public)
                      ?? t.GetProperty("GetProcessId", BindingFlags.Instance | BindingFlags.Public);

        if (propGetPid != null)
        {
            var v = propGetPid.GetValue(session);
            if (v != null) return Convert.ToInt32(v);
        }

        var propPid = t.GetProperty("ProcessID", BindingFlags.Instance | BindingFlags.Public)
                   ?? t.GetProperty("ProcessId", BindingFlags.Instance | BindingFlags.Public);

        if (propPid != null)
        {
            var v = propPid.GetValue(session);
            if (v != null) return Convert.ToInt32(v);
        }

        var m = t.GetMethod("GetProcessID", BindingFlags.Instance | BindingFlags.Public)
             ?? t.GetMethod("GetProcessId", BindingFlags.Instance | BindingFlags.Public);

        if (m != null)
        {
            var v = m.Invoke(session, null);
            if (v != null) return Convert.ToInt32(v);
        }

        return 0;
    }

    private static string? TryGetExeFromIconPath(AudioSessionControl session)
    {
        var t = session.GetType();
        var prop = t.GetProperty("IconPath", BindingFlags.Instance | BindingFlags.Public);
        if (prop == null) return null;

        var v = prop.GetValue(session) as string;
        if (string.IsNullOrWhiteSpace(v)) return null;

        try
        {
            var first = v.Split(',')[0].Trim().Trim('"');
            if (first.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(first);
        }
        catch { }

        return null;
    }

    public static List<AudioSessionInfo> ListSessions()
    {
        var result = new List<AudioSessionInfo>();
        using var device = GetDefaultDevice();

        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];

            int pid = TryGetProcessId(s);
            string exe = "unknown.exe";

            if (pid > 0)
            {
                try { exe = Process.GetProcessById(pid).ProcessName + ".exe"; }
                catch { }
            }
            else
            {
                exe = TryGetExeFromIconPath(s) ?? "unknown.exe";
            }

            var display = string.IsNullOrWhiteSpace(s.DisplayName) ? exe : s.DisplayName;

            result.Add(new AudioSessionInfo
            {
                ProcessId = pid,
                ProcessName = exe,
                DisplayName = display
            });
        }

        return result;
    }

    public static int ApplyVolumes(float? masterVolume01, IReadOnlyDictionary<string, float> exeVolumes01)
    {
        int applied = 0;
        using var device = GetDefaultDevice();

        if (masterVolume01.HasValue)
        {
            var mv = Math.Clamp(masterVolume01.Value, 0f, 1f);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = mv;
        }

        if (exeVolumes01.Count == 0) return applied;

        var sessions = device.AudioSessionManager.Sessions;

        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];

            int pid = TryGetProcessId(s);
            string? pname = null;

            if (pid > 0)
            {
                try { pname = Process.GetProcessById(pid).ProcessName + ".exe"; }
                catch { }
            }
            else
            {
                pname = TryGetExeFromIconPath(s);
            }

            if (string.IsNullOrWhiteSpace(pname)) continue;

            if (exeVolumes01.TryGetValue(pname, out var vol))
            {
                s.SimpleAudioVolume.Volume = Math.Clamp(vol, 0f, 1f);
                applied++;
            }
        }

        return applied;
    }
}
