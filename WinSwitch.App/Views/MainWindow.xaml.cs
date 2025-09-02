using System.Windows;
using WinSwitch.ViewModels;

namespace WinSwitch.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var services = ((App)System.Windows.Application.Current).Services;
        DataContext = services.GetService(typeof(MainViewModel)) as MainViewModel
                      ?? new MainViewModel();
    }
}
