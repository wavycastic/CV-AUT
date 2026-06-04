using System;
using System.Collections.Generic;
using System.Linq;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class LogsViewModel : ViewModelBase
    {
        private readonly IBotService _botService;
        private string _logsText = string.Empty;
        private readonly List<string> _logLines = new();

        public string LogsText
        {
            get => _logsText;
            set => SetProperty(ref _logsText, value);
        }

        public LogsViewModel(IBotService botService)
        {
            _botService = botService;
            _botService.LogReceived += AddLogLine;
        }

        private void AddLogLine(string line)
        {
            _logLines.Add(line);
            if (_logLines.Count > 1000)
            {
                _logLines.RemoveAt(0);
            }
            
            // Re-render full logs text
            LogsText = string.Join("\r\n", _logLines);
        }

        public void ClearLogs()
        {
            _logLines.Clear();
            LogsText = string.Empty;
        }
    }
}
