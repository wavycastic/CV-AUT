using System;
using System.Windows.Input;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// Triển khai ICommand cơ bản không tham số để liên kết các hành động (Actions) từ View tới ViewModel trong mô hình MVVM.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        /// <summary>
        /// Sự kiện xảy ra khi điều kiện thực thi lệnh thay đổi.
        /// Sử dụng CommandManager.RequerySuggested để WPF tự động truy vấn lại CanExecute khi có thay đổi trạng thái UI.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Khởi tạo một RelayCommand mới.
        /// </summary>
        /// <param name="execute">Ủy nhiệm (Delegate) thực thi hành động.</param>
        /// <param name="canExecute">Ủy nhiệm kiểm tra điều kiện thực thi (tùy chọn).</param>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Xác định xem lệnh có thể thực thi ở trạng thái hiện tại hay không.
        /// </summary>
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        /// <summary>
        /// Thực thi lệnh.
        /// </summary>
        public void Execute(object? parameter) => _execute();
    }

    /// <summary>
    /// Triển khai ICommand có tham số kiểu T để liên kết các hành động truyền tham số từ View tới ViewModel.
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu của tham số truyền vào.</typeparam>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        /// <summary>
        /// Sự kiện xảy ra khi điều kiện thực thi thay đổi.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Khởi tạo một RelayCommand có tham số mới.
        /// </summary>
        /// <param name="execute">Hành động thực thi nhận tham số kiểu T.</param>
        /// <param name="canExecute">Kiểm tra điều kiện thực thi nhận tham số kiểu T (tùy chọn).</param>
        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Kiểm tra xem lệnh có thể thực thi với tham số truyền vào hay không.
        /// </summary>
        public bool CanExecute(object? parameter)
        {
            if (_canExecute == null) return true;
            if (parameter == null && typeof(T).IsValueType) return false;
            return _canExecute((T)parameter!);
        }

        /// <summary>
        /// Thực thi lệnh với tham số truyền vào.
        /// </summary>
        public void Execute(object? parameter)
        {
            if (parameter != null || !typeof(T).IsValueType)
            {
                _execute((T)parameter!);
            }
        }
    }
}
