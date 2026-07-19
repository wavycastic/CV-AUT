using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CvAut.ViewModels;

namespace CvAut.Views
{
    public partial class LogsView : UserControl
    {
        private LogsViewModel? _attachedViewModel;

        public LogsView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => AttachViewModel(DataContext as LogsViewModel);
        }

        private void AttachViewModel(LogsViewModel? viewModel)
        {
            if (_attachedViewModel is not null)
            {
                _attachedViewModel.CopyRequested -= OnCopyRequested;
                _attachedViewModel.FilteredLogs.CollectionChanged -= OnFilteredLogsChanged;
                _attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _attachedViewModel = viewModel;
            if (_attachedViewModel is not null)
            {
                _attachedViewModel.CopyRequested += OnCopyRequested;
                _attachedViewModel.FilteredLogs.CollectionChanged += OnFilteredLogsChanged;
                _attachedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnFilteredLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScrollToLastLog();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogsViewModel.AutoScroll))
            {
                ScrollToLastLog();
            }
        }

        private void ScrollToLastLog()
        {
            if (_attachedViewModel?.AutoScroll != true || _attachedViewModel.FilteredLogs.Count == 0)
            {
                return;
            }

            Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(_attachedViewModel.FilteredLogs[^1]));
        }

        private async void OnCopyRequested(string text)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            string tempFile = Path.Combine(Path.GetTempPath(), "Simplimixi_Logs.txt");
            try
            {
                // Write current logs to a temporary text file
                File.WriteAllText(tempFile, text);

                // Get the StorageFile object for this path
                var fileUri = new Uri(tempFile);
                var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(fileUri);

                if (file != null && topLevel.Clipboard is not null)
                {
                    // Copy the file object to clipboard
                    await topLevel.Clipboard.SetFilesAsync(new[] { file });
                    return;
                }
            }
            catch
            {
                // Fallback to text copy if temp file write or file copy fails
            }

            if (topLevel.Clipboard is not null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }
    }
}
