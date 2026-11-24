using System.Windows;

namespace HddSpindown;

public enum DialogResultKind { Primary, Secondary, Close }

/// <summary>
/// Confirmation in the style of the Windows 11 ContentDialog instead of the grey system MessageBox.
/// Enter = primary action, Esc = close. While the dialog is open, the main window is dimmed.
/// </summary>
public partial class FluentDialog : Window
{
    private DialogResultKind _result = DialogResultKind.Close;

    private FluentDialog() => InitializeComponent();

    /// <param name="glyph">Glyph from Segoe Fluent Icons, e.g. "" (warning) or "" (info).</param>
    public static DialogResultKind Show(Window? owner, string title, string message, string primary,
        string? secondary = null, string? close = null, string? note = null, string glyph = "")
    {
        var dlg = new FluentDialog();
        dlg.Title = title;
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.IconGlyph.Text = glyph;
        if (note is not null) { dlg.NoteText.Text = note; dlg.NoteText.Visibility = Visibility.Visible; }

        dlg.PrimaryButton.Content = primary;
        dlg.SecondaryButton.Content = secondary;
        dlg.CloseButton.Content = close;
        if (secondary is null) dlg.Buttons.Children.Remove(dlg.SecondaryButton);
        if (close is null) dlg.Buttons.Children.Remove(dlg.CloseButton);
        dlg.Buttons.Columns = dlg.Buttons.Children.Count;

        bool hasOwner = owner is { IsVisible: true };
        if (hasOwner)
        {
            dlg.Owner = owner;
            if (owner is MainWindow main) main.SetSmoke(true);
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dlg.Topmost = true;
        }

        try { dlg.ShowDialog(); }
        finally { if (owner is MainWindow main) main.SetSmoke(false); }
        return dlg._result;
    }

    private void Primary_Click(object sender, RoutedEventArgs e) { _result = DialogResultKind.Primary; Close(); }
    private void Secondary_Click(object sender, RoutedEventArgs e) { _result = DialogResultKind.Secondary; Close(); }
    private void Close_Click(object sender, RoutedEventArgs e) { _result = DialogResultKind.Close; Close(); }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        try { DragMove(); } catch { /* only possible while the mouse button is pressed */ }
    }
}
