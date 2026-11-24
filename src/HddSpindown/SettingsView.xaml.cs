using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HddSpindown.Services;

namespace HddSpindown;

/// <summary>Settings in the main window: features, help, licenses & privacy, support.</summary>
public partial class SettingsView : UserControl
{
    private sealed record NavEntry(string Glyph, Brush Color, string Label, FrameworkElement Page, bool Animated = false)
    {
        public bool ShowGlyph => !Animated;
    }

    private AppSettings? _settings;

    /// <summary>Back to the drive view.</summary>
    public event Action? BackRequested;

    /// <summary>Option "device path on hover" was toggled.</summary>
    public event Action<bool>? TooltipsChanged;

    /// <summary>"Restart now" after a language change.</summary>
    public event Action? RestartRequested;

    // ---------- Compact navigation in a narrow window ----------

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(SettingsView), new PropertyMetadata(false));

    /// <summary>Icon-only navigation (like Windows 11 Settings in narrow windows).</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private const double CompactBelow = 960;

    private void UpdateCompact()
    {
        bool compact = ActualWidth > 0 && ActualWidth < CompactBelow;
        if (compact == IsCompact && NavColumn.Width.Value > 0) return;
        IsCompact = compact;
        NavColumn.Width = new GridLength(compact ? 68 : 260);
        SettingsCaption.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed record LanguageOption(string Code, string Label);
    private string _startupLanguage = "auto";
    private bool _initializing;

    public SettingsView()
    {
        InitializeComponent();
        // Colored icons like in Windows 11 Settings
        Nav.ItemsSource = new[]
        {
            new NavEntry("", Hex("#8A8F98"), Loc.Features, FeaturesPage),
            new NavEntry("", Hex("#4CC2FF"), Loc.Help, HelpPage),
            new NavEntry("", Hex("#F7A43B"), Loc.LicensesNav, LicensePage),
            new NavEntry("", Hex("#6CCB5F"), Loc.Support, SupportPage, Animated: true),
        };
        HelpList.ItemsSource = SettingsContent.HelpTopics();
        LicenseList.ItemsSource = SettingsContent.Licenses();
        PrivacyBox.Content = SettingsContent.Privacy();
        SetupLink(GitHubLink, AppInfo.GitHubUrl);
        SetupLink(IssueLink, AppInfo.GitHubIssuesUrl);
        Nav.SelectedIndex = 0;

        // Accordion: when one section opens, all others close.
        AddHandler(Expander.ExpandedEvent, new RoutedEventHandler(OnExpanderExpanded));
        SizeChanged += (_, _) => UpdateCompact();
    }

    private void OnExpanderExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Expander opened) return;
        foreach (var other in FindChildren<Expander>(this))
            if (!ReferenceEquals(other, opened)) other.IsExpanded = false;
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var deeper in FindChildren<T>(child)) yield return deeper;
        }
    }

    // ---------- Links ----------

    /// <summary>Empty URL or "#" = placeholder: link greyed out with a "link coming later" note.</summary>
    private static void SetupLink(Button link, string url)
    {
        link.IsEnabled = IsRealUrl(url);
        link.ToolTip = link.IsEnabled ? url : Loc.LinkFollows;
        ToolTipService.SetShowOnDisabled(link, true);
    }

    private void Kofi_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.KofiUrl);
    private void GitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.GitHubUrl);
    private void Issue_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.GitHubIssuesUrl);

    /// <summary>
    /// Opens a web page in the default browser. Via explorer.exe so the browser does NOT run with this
    /// app's admin rights (Explorer hands the call over to the normal user session).
    /// </summary>
    /// <summary>Empty or "#" counts as a placeholder.</summary>
    private static bool IsRealUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    private static void OpenUrl(string url)
    {
        if (!IsRealUrl(url)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = false }); } catch { }
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string command) Copy(command);
    }

    public void Initialize(AppSettings settings)
    {
        _settings = settings;
        _initializing = true;
        _startupLanguage = settings.Language;
        var options = new[] { new LanguageOption("auto", Loc.LangAuto), new LanguageOption("de", "Deutsch"), new LanguageOption("en", "English") };
        LanguageBox.ItemsSource = options;
        LanguageBox.SelectedItem = options.FirstOrDefault(o => o.Code == settings.Language) ?? options[0];
        _initializing = false;
        TooltipSwitch.IsChecked = settings.ShowDiskTooltips;
        NotifySwitch.IsChecked = settings.Notifications;
    }

    /// <summary>On open, start with "Features" and re-query the autostart state.</summary>
    public void Reset()
    {
        Nav.SelectedIndex = 0;
        AutostartError.Visibility = Visibility.Collapsed;
        try { AutostartSwitch.IsChecked = Autostart.IsEnabled(); } catch { AutostartSwitch.IsChecked = false; }
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavEntry selected) return;
        foreach (NavEntry entry in Nav.Items)
            entry.Page.Visibility = entry == selected ? Visibility.Visible : Visibility.Collapsed;
        ContentScroll.ScrollToTop();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    // ---------- Options ----------

    private void TooltipSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        _settings.ShowDiskTooltips = TooltipSwitch.IsChecked == true;
        _settings.Save();
        TooltipsChanged?.Invoke(_settings.ShowDiskTooltips);
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _settings is null || LanguageBox.SelectedItem is not LanguageOption opt) return;
        _settings.Language = opt.Code;
        _settings.Save();
        RestartHint.Visibility = opt.Code == _startupLanguage ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => RestartRequested?.Invoke();

    private void NotifySwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        _settings.Notifications = NotifySwitch.IsChecked == true;
        _settings.Save();
    }

    private void AutostartSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool want = AutostartSwitch.IsChecked == true;
        try
        {
            if (want) Autostart.Enable(); else Autostart.Disable();
            AutostartError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            AutostartError.Text = ex.Message;
            AutostartError.Visibility = Visibility.Visible;
        }
        try { AutostartSwitch.IsChecked = Autostart.IsEnabled(); } catch { AutostartSwitch.IsChecked = !want; }
    }

    // ---------- Folders ----------

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppInfo.SettingsFolder);
        OpenFolder(AppInfo.SettingsFolder);
    }

    private void OpenProgramFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppInfo.ProgramFolder);

    private void CopySettingsFolder_Click(object sender, RoutedEventArgs e) => Copy(AppInfo.SettingsFolder);
    private void CopyProgramFolder_Click(object sender, RoutedEventArgs e) => Copy(AppInfo.ProgramFolder);

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { /* clipboard briefly busy, not critical */ }
    }

    private static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    private static SolidColorBrush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
