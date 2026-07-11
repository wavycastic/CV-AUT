using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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
            if (topLevel?.Clipboard is not null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }
    }
}
