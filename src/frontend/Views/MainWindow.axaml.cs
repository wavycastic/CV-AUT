using Avalonia.Controls;
using Avalonia.Interactivity;
using CvAut.ViewModels;

namespace CvAut.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Kicks off the startup device scan here rather than from a view-model setter, so that
        /// constructing <see cref="MainWindowViewModel"/> stays free of ADB side effects.
        /// <see cref="MainWindowViewModel.StartInitialDeviceScan"/> is guarded, so loading the
        /// window more than once does not scan twice.
        /// </summary>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (DataContext is MainWindowViewModel vm)
            {
                vm.StartInitialDeviceScan();
            }
        }
    }
}
