using Adk.Gui.Controls;
using Adk.Gui.Core.Binding;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed partial class CornerRadiusView : UserControl
{
    public CornerRadiusView(CornerRadiusModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    public Border Bound => BoundBorder;
    public Border Uniform => UniformBorder;
    public Border PerCorner => PerCornerBorder;
}

public sealed class CornerRadiusModel : ObservableObject
{
    float _radius = 4;
    public float Radius
    {
        get => _radius;
        set => RaiseAndSetIfChanged(ref _radius, value, nameof(Radius));
    }
}
