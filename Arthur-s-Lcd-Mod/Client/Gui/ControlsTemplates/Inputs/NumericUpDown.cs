using LcdMod.Client.Gui.ControlsTemplates.Basic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using System.Collections.Generic;

namespace LcdMod.Client.Gui.ControlsTemplates.Inputs
{
    public sealed class NumericUpDown : RectangleControl
    {
        static readonly float[] ColumnWidths = { 0.16f, 0.68f, 0.16f };
        static readonly Vector4 ButtonPadding = new Vector4(0.05f, 0.05f, 0.05f, 0.05f);

        readonly Panels.Grid _grid;
        readonly Button[] _buttons = new Button[2];
        readonly TextInput _textInput;

        public NumericUpDown(RectangleF bounds, NumericUpDownModel model = null)
            : base(bounds, CursorType.Default, model ?? new NumericUpDownModel())
        {
            _grid = new Panels.Grid(bounds, ColumnWidths);
            AddChild(_grid);

            _buttons[0] = CreateStepButton(-1d);

            _textInput = new TextInput(default(RectangleF), new TextInputModel());
            _textInput.Padding = ButtonPadding;
            _grid.AddChild(_buttons[0]);
            _grid.AddChild(_textInput);

            _buttons[1] = CreateStepButton(1d);
            _grid.AddChild(_buttons[1]);

            ConfigureChildModels();
            _grid.SetRect(bounds);
        }

        public NumericUpDownModel NumericModel => DataContext as NumericUpDownModel;

        public override void SetRect(RectangleF bounds)
        {
            base.SetRect(bounds);
            _grid.SetRect(bounds);
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            ConfigureChildModels();
            _grid.Render(sprites);
        }

        Button CreateStepButton(double direction)
        {
            var model = new ButtonModel();
            model.Clicked = delegate { ApplyStep(direction); };
            var button = new Button(default(RectangleF), model);
            button.Padding = ButtonPadding;
            return button;
        }

        void ConfigureChildModels()
        {
            var model = NumericModel;
            if (model == null)
                return;

            double step = GetCurrentStep(model);

            for (int i = 0; i < _buttons.Length; i++)
            {
                var buttonModel = _buttons[i].DataContext as ButtonModel;
                if (buttonModel == null)
                    continue;

                buttonModel.Enabled = model.Enabled;
                _buttons[i].SetEnabled(model.Enabled);
                buttonModel.Text = FormatStep(i == 0 ? -step : step);
            }

            var textModel = _textInput.TextModel;
            if (textModel != null)
            {
                textModel.Enabled = model.Enabled;
                textModel.Value = model.GetText();
                textModel.Title = string.IsNullOrEmpty(model.Title) ? "Number" : model.Title;
                textModel.Subtitle = model.Subtitle;
                textModel.ValueChanged = OnTextValueChanged;
            }
        }

        void ApplyStep(double direction)
        {
            var model = NumericModel;
            if (model == null || !model.Enabled)
                return;

            model.Add(direction * GetCurrentStep(model));
            MarkDirty();
        }

        void OnTextValueChanged(string text)
        {
            var model = NumericModel;
            if (model == null || !model.Enabled)
                return;

            if (model.TrySetValue(text))
                MarkDirty();
        }

        static string FormatStep(double value)
        {
            return value > 0d ? "+" + value.ToString("0.###") : value.ToString("0.###");
        }

        static double GetCurrentStep(NumericUpDownModel model)
        {
            double step = model != null && model.Step > 0d ? model.Step : 1d;
            return step * GetModifierStepMultiplier();
        }

        static int GetModifierStepMultiplier()
        {
            var input = MyAPIGateway.Input;
            bool ctrl = input != null && input.IsAnyCtrlKeyPressed();
            bool shift = input != null && input.IsAnyShiftKeyPressed();

            if (ctrl && shift)
                return 1000;
            if (shift)
                return 100;
            if (ctrl)
                return 10;
            return 1;
        }
    }
}
