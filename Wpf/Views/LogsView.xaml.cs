using CvAut.WpfApp.ViewModels;

namespace CvAut.WpfApp.Views
{
    public partial class LogsView : System.Windows.Controls.UserControl
    {
        public LogsView()
        {
            InitializeComponent();
        }

        private void OnClearClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is LogsViewModel vm)
            {
                vm.ClearLogs();
            }
        }

    }
}
