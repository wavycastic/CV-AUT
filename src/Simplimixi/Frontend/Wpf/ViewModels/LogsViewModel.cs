using System;
using System.Collections.Generic;
using System.Linq;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Nhật ký Logs" (LogsView.xaml).
    /// Quản lý dữ liệu nhật ký hoạt động đầy đủ được truyền từ BotService,
    /// lưu trữ bộ đệm tối đa 1000 dòng và hỗ trợ làm sạch logs.
    /// </summary>
    public class LogsViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;
        
        // Chuỗi văn bản chứa toàn bộ nội dung logs hiển thị lên màn hình
        private string _logsText = string.Empty;
        
        // Bộ nhớ lưu trữ danh sách các dòng log hiện tại
        private readonly List<string> _logLines = new();

        /// <summary>
        /// Chuỗi văn bản đầy đủ chứa toàn bộ nhật ký hiển thị lên UI RichTextBox/TextBox.
        /// </summary>
        public string LogsText
        {
            get => _logsText;
            set => SetProperty(ref _logsText, value);
        }

        /// <summary>
        /// Khởi tạo LogsViewModel và đăng ký nhận log từ BotService.
        /// </summary>
        /// <param name="botService">Dịch vụ quản lý bot.</param>
        public LogsViewModel(IBotService botService)
        {
            _botService = botService;
            
            // Đăng ký nhận log để hiển thị trên trang xem Log chi tiết
            _botService.LogReceived += AddLogLine;
        }

        /// <summary>
        /// Thêm một dòng nhật ký mới vào bộ đệm, giới hạn tối đa 1000 dòng log mới nhất để tránh lag giao diện.
        /// </summary>
        /// <param name="line">Dòng thông tin log mới.</param>
        private void AddLogLine(string line)
        {
            _logLines.Add(line);
            
            // Nếu vượt quá 1000 dòng log, thực hiện xóa dòng cũ nhất
            if (_logLines.Count > 1000)
            {
                _logLines.RemoveAt(0);
            }
            
            // Tạo lại chuỗi văn bản gộp từ các dòng log
            LogsText = string.Join("\r\n", _logLines);
        }

        /// <summary>
        /// Xóa sạch toàn bộ lịch sử log đang hiển thị trên trang nhật ký.
        /// </summary>
        public void ClearLogs()
        {
            _logLines.Clear();
            LogsText = string.Empty;
        }
    }
}
