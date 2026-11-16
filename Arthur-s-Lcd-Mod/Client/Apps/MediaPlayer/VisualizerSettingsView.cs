using Acrux.Controls;
using Acrux.Core;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed partial class VisualizerSettingsView : Window
    {
        public VisualizerSettingsView()
        {
            Resources.Add("EnumEquals", new Views.EnumEqualsConverter());
            InitializeComponent();
        }

        public VisualizerSettingsView(VisualizerSettingsViewModel model) : this()
        {
            DataContext = model;
        }
    }

    /// <summary>Hosts the typed ADK combo box in a settings row's data template.</summary>
    internal sealed class VisualizerColorModePicker : UserControl
    {
        readonly ComboBox _combo;

        public VisualizerColorModePicker()
        {
            _combo = new ComboBox(
                new[] { VisualizerColorMode.Fixed, VisualizerColorMode.Rgb, VisualizerColorMode.TimeBased },
                item => (VisualizerColorMode)item == VisualizerColorMode.Fixed ? "Fixed" :
                    (VisualizerColorMode)item == VisualizerColorMode.Rgb ? "RGB" : "Time-Based",
                item =>
                {
                    var row = DataContext as VisualizerSettingRow;
                    if (row != null)
                        row.ColorMode = (VisualizerColorMode)item;
                })
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Content = _combo;
            Bind<VisualizerSettingRow, VisualizerColorMode>(row => row.ColorMode,
                mode => _combo.SelectedItem = mode, nameof(VisualizerSettingRow.ColorMode));
            Bind<VisualizerSettingRow, float>(row => row.FontSize,
                size => _combo.TextScale = size, nameof(VisualizerSettingRow.FontSize));
        }

        internal ComboBox ComboBox => _combo;
    }
}
