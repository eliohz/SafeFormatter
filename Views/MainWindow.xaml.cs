using SafeFormatter.Models;
using SafeFormatter.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SafeFormatter.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnTileClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                if (sender is ContentPresenter cp && cp.Content is DiskInfo di)
                {
                    vm.Selected = di;
                }
            }
        }
    }
}