using Adk.Gui.Controls;
using Adk.Gui.Core;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed partial class VisualizerSettingsView : Window
    {
        public VisualizerSettingsView(VisualizerSettingsViewModel model)
        {
            Resources.Add("EnumEquals", new Views.EnumEqualsConverter());
            DataContext = model;
            InitializeComponent();
        }
    }

    /// <summary>Hosts the typed ADK combo box in a settings row's data template.</summary>
    internal sealed class VisualizerColorModePicker : UserControl
    {
        readonly ComboBox<VisualizerColorMode> _combo;

        public VisualizerColorModePicker()
        {
            _combo = new ComboBox<VisualizerColorMode>(
                new[] { VisualizerColorMode.Fixed, VisualizerColorMode.Rgb, VisualizerColorMode.TimeBased },
                mode => mode == VisualizerColorMode.Fixed ? "Fixed" :
                    mode == VisualizerColorMode.Rgb ? "RGB" : "Time-Based",
                mode =>
                {
                    var row = DataContext as VisualizerSettingRow;
                    if (row != null)
                        row.ColorMode = mode;
                })
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Content = _combo;
            Bind<VisualizerSettingRow, VisualizerColorMode>(row => row.ColorMode,
                mode => _combo.SetSelectedValue(mode), nameof(VisualizerSettingRow.ColorMode));
            Bind<VisualizerSettingRow, float>(row => row.FontSize,
                size => _combo.TextScale = size, nameof(VisualizerSettingRow.FontSize));
        }

        internal ComboBox<VisualizerColorMode> ComboBox => _combo;
    }
}
