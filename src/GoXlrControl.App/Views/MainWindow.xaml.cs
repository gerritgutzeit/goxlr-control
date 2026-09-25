using System.ComponentModel;
using System.Windows;
using GoXlrControl.App.ViewModels;

namespace GoXlrControl.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_vm.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
