using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// Lớp cơ sở (Base Class) cho tất cả các ViewModel trong ứng dụng.
    /// Triển khai giao diện INotifyPropertyChanged để hỗ trợ cơ chế tự động thông báo thay đổi dữ liệu (Data Binding) lên giao diện WPF.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        /// <summary>
        /// Sự kiện xảy ra khi một thuộc tính trên ViewModel thay đổi giá trị.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Kích hoạt sự kiện PropertyChanged để thông báo cho giao diện WPF cập nhật lại liên kết dữ liệu.
        /// </summary>
        /// <param name="propertyName">Tên của thuộc tính thay đổi (tự động lấy từ tên phương thức/thuộc tính gọi hàm nhờ CallerMemberName).</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Hỗ trợ cập nhật giá trị thuộc tính và tự động kích hoạt sự kiện PropertyChanged nếu giá trị mới khác biệt giá trị cũ.
        /// </summary>
        /// <typeparam name="T">Kiểu dữ liệu của thuộc tính.</typeparam>
        /// <param name="storage">Biến lưu trữ (Backing Field) của thuộc tính.</param>
        /// <param name="value">Giá trị mới muốn thiết lập.</param>
        /// <param name="propertyName">Tên của thuộc tính (tự động lấy).</param>
        /// <returns>True nếu giá trị thay đổi và sự kiện được kích hoạt; ngược lại là False.</returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            // Kiểm tra xem giá trị mới có giống giá trị hiện tại không
            if (Equals(storage, value))
            {
                return false;
            }

            // Gán giá trị mới và phát sự kiện thông báo thay đổi
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
