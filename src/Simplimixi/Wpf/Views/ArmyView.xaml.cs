using System.Windows;
using CvAut.WpfApp.ViewModels;

namespace CvAut.WpfApp.Views
{
    public partial class ArmyView : System.Windows.Controls.UserControl
    {
        public ArmyView()
        {
            InitializeComponent();
        }

        private void OnQuickSlotUp(object sender, RoutedEventArgs e)
        {
            if (DataContext is ArmyViewModel vm)
            {
                if (vm.QuickSlot < 2)
                {
                    vm.QuickSlot++;
                }
            }
        }

        private void OnQuickSlotDown(object sender, RoutedEventArgs e)
        {
            if (DataContext is ArmyViewModel vm)
            {
                if (vm.QuickSlot > 1)
                {
                    vm.QuickSlot--;
                }
            }
        }
    }
}
