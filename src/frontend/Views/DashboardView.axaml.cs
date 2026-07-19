using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CvAut.ViewModels;
using CvAut.Models;
using Material.Icons.Avalonia;
using Material.Icons;

namespace CvAut.Views
{
    public partial class DashboardView : UserControl
    {
        private Window? _configWindow;
        private Window? _mainWindow;
        private DashboardViewModel? _attachedViewModel;

        public DashboardView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_attachedViewModel is not null)
            {
                _attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _attachedViewModel.CopyDeviceLogsRequested -= OnCopyDeviceLogsRequested;
            }

            if (DataContext is DashboardViewModel vm)
            {
                _attachedViewModel = vm;
                vm.PropertyChanged += OnViewModelPropertyChanged;
                vm.CopyDeviceLogsRequested += OnCopyDeviceLogsRequested;
            }
            else
            {
                _attachedViewModel = null;
            }
        }

        private async void OnCopyDeviceLogsRequested(string text)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is not null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardViewModel.State))
            {
                if (DataContext is DashboardViewModel vm)
                {
                    if (vm.State == DashboardDeviceState.ConfiguringDevice)
                    {
                        ShowConfigWindow(vm);
                    }
                    else
                    {
                        CloseConfigWindow();
                    }
                }
            }
        }

        private void OnMainWindowPositionChanged(object? sender, PixelPointEventArgs e)
        {
            UpdateConfigWindowPosition();
        }

        private void OnMainWindowSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            UpdateConfigWindowPosition();
        }

        private void UpdateConfigWindowPosition()
        {
            if (_configWindow != null && _mainWindow != null)
            {
                var pos = _mainWindow.Position;
                var scale = _mainWindow.RenderScaling;
                double logicalWidth = _mainWindow.FrameSize?.Width ?? _mainWindow.Width;
                int physicalWidth = (int)(logicalWidth * scale);
                _configWindow.Position = new PixelPoint(pos.X + physicalWidth + 8, pos.Y);
                _configWindow.Height = _mainWindow.Height;
            }
        }

        private void ShowConfigWindow(DashboardViewModel vm)
        {
            if (_configWindow != null)
            {
                return;
            }

            _mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            
            var settingsView = new SettingsView
            {
                DataContext = vm.SettingsViewModel
            };

            // Custom Title Bar to match MainWindow style
            var titleBarGrid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Left: Title & Icon
            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconBorder = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = (CornerRadius)Application.Current!.FindResource("RadiusSm")!,
                Background = (IBrush)Application.Current!.FindResource("AppAccentBrush")!,
                Child = new MaterialIcon
                {
                    Kind = MaterialIconKind.Robot,
                    Width = 14,
                    Height = 14,
                    Foreground = (IBrush)Application.Current!.FindResource("AppAccentTextBrush")!,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            
            var titleText = new TextBlock
            {
                Text = "Cấu hình - " + (vm.SelectedDeviceForConfig?.DisplayName ?? "Thiết bị"),
                Classes = { "statNum" },
                VerticalAlignment = VerticalAlignment.Center
            };

            leftStack.Children.Add(iconBorder);
            leftStack.Children.Add(titleText);
            Grid.SetColumn(leftStack, 0);
            titleBarGrid.Children.Add(leftStack);

            // Middle/Right: Actions
            var actionStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            actionStack.SetValue(WindowDecorationProperties.ElementRoleProperty, WindowDecorationsElementRole.User);

            var saveBtn = new Button
            {
                Classes = { "accent" },
                Content = "Lưu lại",
                Command = vm.SettingsViewModel.InstanceSaveCommand,
                Height = 30,
                Padding = new Thickness(14, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            saveBtn.SetValue(WindowDecorationProperties.ElementRoleProperty, WindowDecorationsElementRole.User);

            var cancelBtn = new Button
            {
                Content = "Hủy bỏ",
                Command = vm.SettingsViewModel.InstanceCancelCommand,
                Height = 30,
                Padding = new Thickness(14, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            cancelBtn.SetValue(WindowDecorationProperties.ElementRoleProperty, WindowDecorationsElementRole.User);

            actionStack.Children.Add(saveBtn);
            actionStack.Children.Add(cancelBtn);
            Grid.SetColumn(actionStack, 1);
            titleBarGrid.Children.Add(actionStack);

            // Title Bar Border container
            var titleBarBorder = new Border
            {
                Height = 48,
                Padding = new Thickness(12, 0),
                Background = (IBrush)Application.Current!.FindResource("AppSurfaceBrush")!,
                BorderBrush = (IBrush)Application.Current!.FindResource("AppBorderBrush")!,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = titleBarGrid
            };
            titleBarBorder.SetValue(WindowDecorationProperties.ElementRoleProperty, WindowDecorationsElementRole.TitleBar);

            // Accent gradient line underneath the title bar
            var gradientLine = new Border
            {
                Height = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#00d4af37"), 0),
                        new GradientStop(Color.Parse("#d4af37"), 0.3),
                        new GradientStop(Color.Parse("#e6c44a"), 0.5),
                        new GradientStop(Color.Parse("#d4af37"), 0.7),
                        new GradientStop(Color.Parse("#00d4af37"), 1)
                    }
                }
            };

            var headerContainer = new Grid();
            headerContainer.Children.Add(titleBarBorder);
            headerContainer.Children.Add(gradientLine);

            var contentContainer = new Border
            {
                Padding = new Thickness(16),
                Child = settingsView
            };

            var mainGrid = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,*"),
                Background = (IBrush)Application.Current!.FindResource("AppBackgroundBrush")!
            };
            Grid.SetRow(headerContainer, 0);
            Grid.SetRow(contentContainer, 1);
            mainGrid.Children.Add(headerContainer);
            mainGrid.Children.Add(contentContainer);

            _configWindow = new Window
            {
                Title = "Cấu hình thiết bị - " + (vm.SelectedDeviceForConfig?.DisplayName ?? "Thiết bị"),
                Width = 500,
                Height = _mainWindow?.Height ?? 720,
                MinWidth = 450,
                MinHeight = 400,
                CanResize = false,
                ExtendClientAreaToDecorationsHint = true,
                WindowDecorations = WindowDecorations.None,
                Content = mainGrid,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            if (_mainWindow != null)
            {
                _mainWindow.PositionChanged += OnMainWindowPositionChanged;
                _mainWindow.SizeChanged += OnMainWindowSizeChanged;
                UpdateConfigWindowPosition();
            }

            _configWindow.Closed += (s, e) =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.PositionChanged -= OnMainWindowPositionChanged;
                    _mainWindow.SizeChanged -= OnMainWindowSizeChanged;
                }
                _configWindow = null;
                // If we are still in configuring state, cancel it
                if (vm.State == DashboardDeviceState.ConfiguringDevice)
                {
                    vm.SettingsViewModel.InstanceCancelCommand.Execute(null);
                }
            };

            _configWindow.Show();
        }

        private void CloseConfigWindow()
        {
            if (_configWindow != null)
            {
                var win = _configWindow;
                _configWindow = null;
                win.Close();
            }
        }
    }
}
