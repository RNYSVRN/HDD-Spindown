using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace HddSpindown;

/// <summary>
/// Notification area icon. The context menu is a native Win32 menu, so it looks
/// like Windows' own menus (rounded, light/dark matching the app mode).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly MenuOwner _owner = new();
    private IReadOnlyList<DiskItem> _disks = [];

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? WakeAllRequested;
    public event Action? SleepSelectedRequested;
    public event Action<DiskItem>? SleepRequested;
    public event Action<DiskItem>? WakeRequested;

    public TrayIcon()
    {
        EnableDarkMenus();
        _image = CreateIcon();
        _icon = new Forms.NotifyIcon { Icon = _image, Text = Loc.AppName, Visible = true };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Right) ShowMenu(); };
    }

    public void SetText(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    private bool _asleepShown;
    private Icon? _sleepIcon;

    /// <summary>
    /// If at least one drive is asleep, the icon gets the moon in the accent color; otherwise no moon.
    /// The exact count is in the tooltip (see SetText).
    /// </summary>
    public void SetSleepingCount(int count)
    {
        bool asleep = count > 0;
        if (asleep == _asleepShown) return;
        _asleepShown = asleep;
        var old = _sleepIcon;
        _sleepIcon = asleep ? CreateSleepIcon(_image) : null;   // recreated each time: follows a changed accent color
        _icon.Icon = _sleepIcon ?? _image;
        if (old is not null) { DestroyIcon(old.Handle); old.Dispose(); }
    }

    private bool _canWakeAll, _canSleepSelected;

    /// <summary>Remembers the drives and which bulk actions currently make sense (greyed out otherwise).</summary>
    public void Rebuild(IEnumerable<DiskItem> disks, bool canWakeAll, bool canSleepSelected)
    {
        _disks = disks.ToList();
        _canWakeAll = canWakeAll;
        _canSleepSelected = canSleepSelected;
    }

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.None);

    // ---------- native menu ----------

    private void ShowMenu()
    {
        var actions = new Dictionary<int, Action>();
        int nextId = 1;
        int Add(Action a) { actions[nextId] = a; return nextId++; }

        IntPtr menu = CreatePopupMenu();
        try
        {
            int openId = Add(() => OpenRequested?.Invoke());
            AppendMenu(menu, MF_STRING, openId, Loc.T("Öffnen", "Open"));
            SetMenuDefaultItem(menu, openId, 0); // bold like in Windows
            AppendMenu(menu, MF_SEPARATOR, 0, null);

            foreach (var d in _disks.Where(d => !d.IsReadOnly && !d.Info.IsUnavailable))
            {
                IntPtr sub = CreatePopupMenu();
                AppendMenu(sub, MF_STRING | (d.CanSleep ? 0 : MF_GRAYED), Add(() => SleepRequested?.Invoke(d)),
                    Loc.T("Schlafen legen", "Spin down"));
                AppendMenu(sub, MF_STRING | (d.CanWake ? 0 : MF_GRAYED), Add(() => WakeRequested?.Invoke(d)),
                    Loc.T("Aufwecken", "Wake up"));

                string tags = d.Tags.Count > 0 ? $"  [{d.TagsText}]" : "";
                string label = $"{d.Title}  ({d.LettersText}){tags}  –  {d.StateText}";
                AppendMenu(menu, MF_POPUP | (d.IsLocked ? MF_CHECKED : 0), sub, label);
            }

            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING | (_canSleepSelected ? 0 : MF_GRAYED), Add(() => SleepSelectedRequested?.Invoke()),
                Loc.T("Ausgewählte schlafen", "Spin down selected"));
            AppendMenu(menu, MF_STRING | (_canWakeAll ? 0 : MF_GRAYED), Add(() => WakeAllRequested?.Invoke()), Loc.T("Alle aufwecken", "Wake all"));
            AppendMenu(menu, MF_STRING, Add(() => ExitRequested?.Invoke()), Loc.T("Beenden", "Exit"));

            FlushMenuThemes();
            GetCursorPos(out POINT pt);
            // Without a foreground window, the menu doesn't close when clicking outside it.
            SetForegroundWindow(_owner.Handle);
            int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, pt.X, pt.Y, _owner.Handle, IntPtr.Zero);
            PostMessage(_owner.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd != 0 && actions.TryGetValue(cmd, out var action)) action();
        }
        finally
        {
            DestroyMenu(menu); // also destroys the attached submenus
        }
    }

    /// <summary>Invisible window as the menu's owner.</summary>
    private sealed class MenuOwner : Forms.NativeWindow, IDisposable
    {
        public MenuOwner()
        {
            CreateHandle(new Forms.CreateParams());
            try { AllowDarkModeForWindow(Handle, true); } catch { }
        }
        public void Dispose() => DestroyHandle();
    }

    /// <summary>
    /// Makes Win32 menus follow the Windows light/dark app mode.
    /// uxtheme ordinals 135/136 (SetPreferredAppMode/FlushMenuThemes) are undocumented,
    /// but stable since Windows 10 1903 and used by Explorer extensions, among others.
    /// </summary>
    private static void EnableDarkMenus()
    {
        try { SetPreferredAppMode(1 /* AllowDark */); } catch { /* older Windows versions: light */ }
    }

    private static void FlushMenuThemes()
    {
        try { FlushMenuThemesNative(); } catch { }
    }

    // ---------- Icon ----------

    /// <summary>Tray icon without the moon (all drives awake), matching the notification area size.</summary>
    private static Icon CreateIcon()
    {
        // Fall back to the app icon if the tray icon is missing; the app must never fail because of it.
        foreach (var name in new[] { "tray_awake.ico", "app.ico" })
        {
            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
                if (info is null) continue;
                using var stream = info.Stream;
                return new Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
            catch (System.IO.IOException) { }
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    /// <summary>
    /// Tray icon "something is asleep": the same icon with a moon badge at the bottom right in the accent color
    /// (like in the app icon). Drawn with 4x supersampling so it stays clean even at 16 px.
    /// </summary>
    private static Icon CreateSleepIcon(Icon baseIcon)
    {
        const int ss = 4;
        int size = baseIcon.Width, big = size * ss;
        using var hi = new Bitmap(big, big, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(hi))
        {
            // Upscale the base icon pixel-exact (keeps the drawing optimized for small sizes)
            using (var baseBmp = baseIcon.ToBitmap())
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(baseBmp, new Rectangle(0, 0, big, big));
            }
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.ScaleTransform(big, big); // from here on 0..1 like in the icon generator

            var c = new PointF(0.70f, 0.70f);
            float r = 0.30f;
            var badge = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);

            // cut out instead of a white border: looks calmer in the notification area
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(Color.Transparent))
                g.FillEllipse(clear, RectangleF.Inflate(badge, 0.06f, 0.06f));
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

            var accent = AccentColor();
            using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(badge, Lighten(accent, 0.18f), accent, 90f))
                g.FillEllipse(fill, badge);

            // Crescent: large circle minus an offset circle
            double lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
            var moonColor = lum > 0.6 ? Color.FromArgb(32, 32, 32) : Color.White;
            float mr = 0.165f;
            var moon = new RectangleF(c.X - mr - 0.012f, c.Y - mr + 0.006f, 2 * mr, 2 * mr);
            float cr = 0.14f;
            var cut = new RectangleF(moon.X + 0.115f, moon.Y - 0.045f, 2 * cr, 2 * cr);
            using var crescent = new System.Drawing.Drawing2D.GraphicsPath(System.Drawing.Drawing2D.FillMode.Alternate);
            crescent.AddEllipse(moon);
            crescent.AddEllipse(cut);
            var state = g.Save();
            using (var clip = new System.Drawing.Drawing2D.GraphicsPath()) { clip.AddEllipse(moon); g.SetClip(clip); }
            using (var mb = new SolidBrush(moonColor)) g.FillPath(mb, crescent);
            g.Restore(state);
        }

        using var small = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.DrawImage(hi, new Rectangle(0, 0, size, size));
        }
        return Icon.FromHandle(small.GetHicon());
    }

    private static Color Lighten(Color c, float amount) => Color.FromArgb(255,
        (int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

    /// <summary>The same accent color as the app UI (WPF), else the registry, else Windows blue.</summary>
    private static Color AccentColor()
    {
        try
        {
            var c = System.Windows.SystemColors.AccentColor;
            if (c.A > 0) return Color.FromArgb(255, c.R, c.G, c.B);
        }
        catch { }
        try
        {
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr)
                return Color.FromArgb(255, abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
        }
        catch { }
        return Color.FromArgb(0x00, 0x67, 0xC0);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
        if (_sleepIcon is not null) { DestroyIcon(_sleepIcon.Handle); _sleepIcon.Dispose(); }
        _owner.Dispose();
    }

    // ---------- Win32 ----------

    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    private const uint WM_NULL = 0;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nint id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, int item, int byPos);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("uxtheme.dll", EntryPoint = "#133")] private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemesNative();
}
