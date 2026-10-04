using System;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Rendering;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Projector
{
    internal sealed partial class ProjectorView : UserControl
    {
        ProjectorViewModel _model;
        Inventory.InventoryView _inventory;
        ComboBox<ProjectorListMode> _modeComboBox;

        public ProjectorView()
        {
            Resources.Add("FontSizeMultiplier", new Views.FontSizeMultiplierConverter());
            Resources.Add("Multiply", new Views.MultiplyConverter());
            Resources.Add("projectorButtonFontSize", (Func<float>)(() => .58f * FontScale));
            InitializeComponent();
        }

        float FontScale
        {
            get { float value; return TryFindResource("fontScale", out value) ? value : 1f; }
        }

        public ProjectorView(ProjectorViewModel model) : this()
        {
            _model = model;
            DataContext = model;
            AttachInventory();
            AttachModeComboBox();
            model.PropertyChanged += OnModelChanged;
        }

        void AttachModeComboBox()
        {
            if (_model == null) return;
            if (_modeComboBox == null)
            {
                _modeComboBox = new ComboBox<ProjectorListMode>(_model.Modes, mode => _model.ModeLabel(mode),
                    mode => { if (_model.SelectMode != null) _model.SelectMode(mode); })
                {
                    OpenDirection = ComboBoxOpenDirection.Up,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                _modeComboBox.SetSelectedValue(_model.Mode);
            }
            ModeHost.Child = _modeComboBox;
        }

        void OnModelChanged(ObservableObject sender, string property)
        {
            if (property == nameof(ProjectorViewModel.Mode) && _modeComboBox != null)
                _modeComboBox.SetSelectedValue(_model.Mode);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_modeComboBox != null)
                _modeComboBox.TextScale = .58f * FontScale;
            return base.MeasureOverride(availableSize);
        }

        public override void Dispose()
        {
            if (_model != null)
                _model.PropertyChanged -= OnModelChanged;
            base.Dispose();
        }

        void AttachInventory()
        {
            if (_model == null) return;
            if (_inventory == null) _inventory = new Inventory.InventoryView(_model.Inventory);
            InventoryPresenter.Content = _inventory;
        }

        partial void OnHotReloaded()
        {
            AttachInventory();
            AttachModeComboBox();
        }

        public bool AdvanceAutoScroll(long frame, float secondsPerRow)
        {
            return _inventory != null && _inventory.AdvanceAutoScroll(frame, secondsPerRow);
        }

        protected override IDisposable BeginChildDraw(RenderContext context, VisualElement child) => context.PushClip(Bounds);
    }
}
