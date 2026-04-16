using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FLARE.UI.Helpers;
using FLARE.UI.ViewModels;
using FLARE.UI.Views;

namespace FLARE.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

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

    private void AboutClick(object sender, MouseButtonEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }
}
