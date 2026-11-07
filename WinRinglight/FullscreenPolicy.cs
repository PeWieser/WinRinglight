using System.Collections.Generic;

namespace WinRinglight;

/// <summary>Resolve top-to-bottom fullscreen windows and taskbars independently.</summary>
public static class FullscreenPolicy
{
    public readonly record struct Entry(int Monitor, bool Taskbar);
    public static HashSet<int> Detect(int monitorCount, IEnumerable<Entry> zOrder, int? foregroundShellMonitor)
    {
        var resolved = new HashSet<int>();
        var fullscreen = new HashSet<int>();
        foreach (var entry in zOrder)
        {
            if (entry.Monitor < 0 || entry.Monitor >= monitorCount || !resolved.Add(entry.Monitor)) continue;
            if (!entry.Taskbar) fullscreen.Add(entry.Monitor);
        }
        if (foregroundShellMonitor.HasValue) fullscreen.Remove(foregroundShellMonitor.Value);
        return fullscreen;
    }
}
