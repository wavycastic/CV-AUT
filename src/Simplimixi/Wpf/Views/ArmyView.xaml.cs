using System.Windows;
using CvAut.WpfApp.ViewModels;

namespace CvAut.WpfApp.Views
{
    /// <summary>
    /// Logic xử lý (Code-Behind) của ArmyView.xaml.
    /// Quản lý giao diện cấu hình quân đội, cho phép điều chỉnh chỉ số Quick Slot bằng các nút nhấn.
    /// </summary>
    public partial class ArmyView : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Khởi tạo một thực thể mới của ArmyView.
        /// </summary>
        public ArmyView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Tăng chỉ số Quick Slot lên (tối đa bằng 2).
        /// </summary>
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

        /// <summary>
        /// Giảm chỉ số Quick Slot xuống (tối thiểu bằng 1).
        /// </summary>
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
