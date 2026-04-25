using System.ComponentModel;
using System.Reflection;
using System.Windows;
using FLARE.UI.Helpers;
using FLARE.UI.ViewModels;
using FLARE.UI.Views;

namespace FLARE.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        Title = BuildBanner.GetWindowTitle(Assembly.GetExecutingAssembly());

        TitleBarHelper.SetDarkTitleBar(this);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LogOutput))
        {
            LogTextBox.ScrollToEnd();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The settings debounce timer holds any keystrokes made in the last 500 ms.
        // Flush synchronously so "type a value, immediately close" doesn't drop the
        // save. No-op when nothing is pending.
        _viewModel.FlushPendingSave();
        base.OnClosing(e);
    }

    private void AboutClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }
}
