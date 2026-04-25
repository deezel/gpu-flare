using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FLARE.UI.Helpers;

// Attached behavior: SelectAll when a TextBox gains focus, including via mouse click
// (WPF's default handling drops the selection when the click positions the caret).
public static class SelectAllOnFocus
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(SelectAllOnFocus),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue)
        {
            tb.GotKeyboardFocus += OnGotKeyboardFocus;
            tb.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
        else
        {
            tb.GotKeyboardFocus -= OnGotKeyboardFocus;
            tb.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        }
    }

    private static void OnGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    // Intercept the mouse-down on an unfocused TextBox so caret-placement doesn't clobber
    // the SelectAll fired by GotKeyboardFocus. Already-focused falls through so double-click
    // substring selection still works.
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
        {
            tb.Focus();
            e.Handled = true;
        }
    }
}
