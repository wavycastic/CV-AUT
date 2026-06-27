using Avalonia.Controls;

namespace CvAut.Views
{
    /// <summary>
    /// The reusable device panel — binds a <c>DeviceViewModel</c> and shows status, lifecycle
    /// buttons, live stats and the per-device log. Identical for single and grid mode
    /// (roadmap: "DevicePanelView là lõi UI, tái dùng cho single/grid").
    /// </summary>
    public partial class DevicePanelView : UserControl
    {
        public DevicePanelView()
        {
            InitializeComponent();
        }
    }
}
