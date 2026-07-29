using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Common.Helpers;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    /// <summary>A weapon block type the player can give a per-weapon magazine target (keyed by SubtypeId).</summary>
    internal sealed class WeaponOption
    {
        public WeaponOption(string subtypeId, string displayName)
        {
            SubtypeId = subtypeId;
            DisplayName = displayName;
        }

        public string SubtypeId { get; private set; }
        public string DisplayName { get; private set; }
    }

    internal abstract class CargoSettingsDialogBase : Dialog
    {
        protected CargoSettingsDialogBase(IApp parentApp) : base(parentApp)
        {
        }

        protected abstract string TitleKey { get; }
        protected virtual float CardWidthFraction => 0.9f;
        protected virtual float CardHeightFraction => 0.9f;
        protected virtual float CardMinWidth => 220f;
        protected virtual float CardMinHeight => 160f;

        protected abstract void RenderContent(RectangleF contentRect, float scale, float fontScale,
            IMyTextSurface surface);

        protected override void BuildDialogControls(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            var container = EnsureContainer(viewBox);
            container.ClearChildren();

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = surface.TextureSize * 0.5f,
                Size = surface.TextureSize,
                Color = new Color(0, 0, 0, 200),
                Alignment = TextAlignment.CENTER
            });

            var pad = 16f * scale;
            var cardWidth = Math.Min(viewBox.Width - 2f * pad, Math.Max(CardMinWidth * scale, viewBox.Width * CardWidthFraction));
            var cardHeight = Math.Min(viewBox.Height - 2f * pad, Math.Max(CardMinHeight * scale, viewBox.Height * CardHeightFraction));
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            RegisterDialogCard(cardRect);

            var cardMin = Math.Min(cardWidth, cardHeight);
            var cardRadius = cardMin * 0.04f;
            var cardShadow = Math.Max(1f, cardMin * 0.012f);
            BorderRenderer.CreateSpritesFromRect(new RectangleF(cardRect.X + cardShadow, cardRect.Y + cardShadow, cardWidth, cardHeight),
                Sprites, ResolveColor(ThemeResources.ShadowColor), radiusPixels: cardRadius, radiusScale: 1f);
            BorderRenderer.CreateSpritesFromRect(cardRect, Sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusPixels: cardRadius, radiusScale: 1f);

            var titleScale = PadButtonStyle.TextScaleForHeight(
                MathHelper.Clamp(cardRect.Height * 0.06f, 14f, 24f), TextFont, surface);
            var titleHeight = MeasureLineHeight(titleScale, surface);
            var titleText = PadButtonStyle.TrimToWidth(MyTexts.GetString(TitleKey), cardRect.Width - 2f * pad, titleScale, TextFont, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = titleText,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + pad),
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                RotationOrScale = titleScale,
                Alignment = TextAlignment.CENTER,
                FontId = TextFont
            });

            var contentTop = cardRect.Y + pad + titleHeight + 10f * scale;
            var contentRect = new RectangleF(
                cardRect.X + pad,
                contentTop,
                cardRect.Width - 2f * pad,
                Math.Max(0f, cardRect.Bottom - pad - contentTop));

            RenderContent(contentRect, scale, fontScale, surface);
        }

        protected void DrawLeftLabel(string text, Vector2 centerLeft, float maxWidth, float rowHeight, string role)
        {
            var provider = ParentApp as ITextSurfaceProvider;
            var surface = provider?.TextSurface;
            var textScale = PadButtonStyle.TextScaleForHeight(
                MathHelper.Clamp(rowHeight * 0.45f, 11f, 18f), TextFont, surface);
            var trimmed = PadButtonStyle.TrimToWidth(text, maxWidth, textScale, TextFont, surface);
            var height = MeasureLineHeight(textScale, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(centerLeft.X, centerLeft.Y - height * 0.5f),
                Color = ResolveColor(ThemeResources.FromThemeRole(role)),
                RotationOrScale = textScale,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
        }

        protected static string SortModeLabel(int mode)
        {
            switch ((InventorySortMode)mode)
            {
                case InventorySortMode.Weight:
                    return MyTexts.GetString(MOD_PREFIX + "Cargo_Sort_Weight");
                case InventorySortMode.Alphabetical:
                    return MyTexts.GetString(MOD_PREFIX + "Cargo_Sort_Alphabetical");
                default:
                    return MyTexts.GetString(MOD_PREFIX + "Cargo_Sort_Quantity");
            }
        }
    }

    internal sealed class CargoActionsSettingsDialog : CargoSettingsDialogBase
    {
        const int BUTTON_SORT = 0;
        const int BUTTON_URANIUM = 1;
        const int BUTTON_WEAPONS = 2;
        const int BUTTON_COUNT = 3;

        readonly Func<CargoActionsConfigComponent> _getConfig;
        CargoActionsConfigComponent Config => _getConfig();
        readonly Action _onSaved;
        readonly Action _requestRedraw;
        readonly Action<Dialog> _showDialog;
        readonly List<WeaponOption> _weapons;

        readonly Button[] _buttons = new Button[BUTTON_COUNT];

        public CargoActionsSettingsDialog(IApp parentApp, Func<CargoActionsConfigComponent> getConfig, Action onSaved,
            Action requestRedraw, Action<Dialog> showDialog, List<WeaponOption> weapons)
            : base(parentApp)
        {
            _getConfig = getConfig;
            _onSaved = onSaved;
            _requestRedraw = requestRedraw;
            _showDialog = showDialog;
            _weapons = weapons ?? new List<WeaponOption>();
        }

        protected override string TitleKey => MOD_PREFIX + "CargoActions_Config";
        protected override float CardHeightFraction => 0.72f;

        void BackToMenu()
        {
            if (_showDialog != null)
                _showDialog(this);
        }

        protected override void RenderContent(RectangleF contentRect, float scale, float fontScale,
            IMyTextSurface surface)
        {
            var gap = 10f * scale;
            var buttonHeight = Math.Min(110f * scale, (contentRect.Height - 2f * gap) / 3f);

            var sortText = MyTexts.GetString(MOD_PREFIX + "CargoActions_Sort") + ": " + SortModeLabel(Config.SortMode);
            RenderButton(BUTTON_SORT, Rect(contentRect, 0, buttonHeight, gap), sortText, CycleSort);
            RenderButton(BUTTON_URANIUM, Rect(contentRect, 1, buttonHeight, gap), MyTexts.GetString(MOD_PREFIX + "CargoActions_Uranium"), OpenUranium);
            RenderButton(BUTTON_WEAPONS, Rect(contentRect, 2, buttonHeight, gap), MyTexts.GetString(MOD_PREFIX + "CargoActions_Ammo"), OpenWeapons);
        }

        static RectangleF Rect(RectangleF content, int row, float buttonHeight, float gap)
        {
            return new RectangleF(content.X, content.Y + row * (buttonHeight + gap), content.Width, buttonHeight);
        }

        void RenderButton(int index, RectangleF rect, string text, Action onClick)
        {
            var button = _buttons[index];
            if (button == null)
            {
                var click = onClick; 
                button = new Button(rect, new ButtonModel { Text = text, Clicked = delegate { click(); } });
                _buttons[index] = button;
            }
            else
            {
                button.SetRect(rect);
            }

            var model = button.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = text;
                model.Enabled = true;
            }

            button.SetStyleId("Primary");
            button.SetClass("ControlBase Button");
            button.SetCursor(CursorType.Hand);
            button.CustomRender = PadButtonStyle.RenderLabeled;
            button.SetVisible(true);
            ContainerControl.AddChild(button);
            button.Render(Sprites);
        }


        void CycleSort()
        {
            Config.SortMode = (Config.SortMode + 1) % 3;
            if (_onSaved != null)
                _onSaved();
            if (_requestRedraw != null)
                _requestRedraw();
        }

        void OpenUranium()
        {
            Open(new CargoUraniumDialog(ParentApp, _getConfig, _onSaved, _requestRedraw, BackToMenu));
        }

        void OpenWeapons()
        {
            Open(new CargoWeaponsDialog(ParentApp, _getConfig, _onSaved, _requestRedraw, BackToMenu, _weapons));
        }

        void Open(Dialog dialog)
        {
            if (_showDialog != null)
                _showDialog(dialog);
        }
    }

    internal sealed class CargoUraniumDialog : CargoSettingsDialogBase
    {
        readonly Func<CargoActionsConfigComponent> _getConfig;
        CargoActionsConfigComponent Config => _getConfig();
        readonly Action _onSaved;
        readonly Action _requestRedraw;

        readonly NumericUpDown[] _inputs = new NumericUpDown[4];
        readonly string[] _labels = new string[4];

        public CargoUraniumDialog(IApp parentApp, Func<CargoActionsConfigComponent> getConfig, Action onSaved,
            Action requestRedraw, Action backToMenu)
            : base(parentApp)
        {
            _getConfig = getConfig;
            _onSaved = onSaved;
            _requestRedraw = requestRedraw;
            OnClose = backToMenu;

            var gridLarge = MyTexts.GetString(MOD_PREFIX + "CargoActions_GridLarge");
            var gridSmall = MyTexts.GetString(MOD_PREFIX + "CargoActions_GridSmall");
            var reactorSmall = MyTexts.GetString(MOD_PREFIX + "CargoActions_ReactorSmall");
            var reactorLarge = MyTexts.GetString(MOD_PREFIX + "CargoActions_ReactorLarge");
            _labels[0] = gridLarge + " / " + reactorSmall;
            _labels[1] = gridLarge + " / " + reactorLarge;
            _labels[2] = gridSmall + " / " + reactorSmall;
            _labels[3] = gridSmall + " / " + reactorLarge;
        }

        protected override string TitleKey => MOD_PREFIX + "CargoActions_Uranium";

        protected override void OnDismiss()
        {
            if (_onSaved != null)
                _onSaved();
        }

        int GetValue(int index)
        {
            switch (index)
            {
                case 0: return Config.UraniumLargeGridSmallReactor;
                case 1: return Config.UraniumLargeGridLargeReactor;
                case 2: return Config.UraniumSmallGridSmallReactor;
                default: return Config.UraniumSmallGridLargeReactor;
            }
        }

        void SetValue(int index, int value)
        {
            switch (index)
            {
                case 0: Config.UraniumLargeGridSmallReactor = value; break;
                case 1: Config.UraniumLargeGridLargeReactor = value; break;
                case 2: Config.UraniumSmallGridSmallReactor = value; break;
                default: Config.UraniumSmallGridLargeReactor = value; break;
            }
        }

        protected override void RenderContent(RectangleF contentRect, float scale, float fontScale,
            IMyTextSurface surface)
        {
            var rowGap = 12f * scale;
            var rowH = Math.Min(40f * scale, (contentRect.Height - 3f * rowGap) / 4f);

            for (int i = 0; i < 4; i++)
            {
                var rowY = contentRect.Y + i * (rowH + rowGap);
                DrawLeftLabel(_labels[i], new Vector2(contentRect.X, rowY + rowH * 0.5f), contentRect.Width * 0.5f, rowH, ON_SURFACE);
                var stepperRect = new RectangleF(contentRect.X + contentRect.Width * 0.55f, rowY, contentRect.Width * 0.45f, rowH);
                RenderInput(i, stepperRect);
            }
        }

        void RenderInput(int index, RectangleF rect)
        {
            var input = _inputs[index];
            if (input == null)
            {
                var model = new NumericUpDownModel
                {
                    Value = GetValue(index),
                    MinValue = 0,
                    MaxValue = 100000,
                    Step = 1,
                    Format = "0",
                    Title = MyTexts.GetString(MOD_PREFIX + "CargoActions_Uranium"),
                    ValueChanged = MakeChanged(index)
                };
                input = new NumericUpDown(rect, model);
                _inputs[index] = input;
            }
            else
            {
                input.SetRect(rect);
                if (input.NumericModel != null)
                    input.NumericModel.Value = GetValue(index);
            }

            input.SetVisible(true);
            ContainerControl.AddChild(input);
            input.Render(Sprites);
        }

        Action<double> MakeChanged(int index)
        {
            return delegate(double value)
            {
                SetValue(index, (int)Math.Round(value));
                if (_requestRedraw != null)
                    _requestRedraw();
            };
        }
    }

    internal sealed class CargoWeaponsDialog : CargoSettingsDialogBase
    {
        readonly Func<CargoActionsConfigComponent> _getConfig;
        CargoActionsConfigComponent Config => _getConfig();
        readonly Action _onSaved;
        readonly Action _requestRedraw;

        readonly Dictionary<string, int> _overrides = new Dictionary<string, int>();
        readonly List<WeaponOption> _options;
        WeaponOption _selected;

        NumericUpDown _defaultInput;
        ListBox<WeaponOption> _list;
        ListBoxModel<WeaponOption> _listModel;
        NumericUpDown _selectedInput;

        public CargoWeaponsDialog(IApp parentApp, Func<CargoActionsConfigComponent> getConfig, Action onSaved,
            Action requestRedraw, Action backToMenu, List<WeaponOption> weapons)
            : base(parentApp)
        {
            _getConfig = getConfig;
            _onSaved = onSaved;
            _requestRedraw = requestRedraw;
            OnClose = backToMenu;
            _options = weapons ?? new List<WeaponOption>();

            var config = Config;
            var keys = config.WeaponOverrideKeys;
            var counts = config.WeaponOverrideCounts;
            if (keys != null && counts != null)
            {
                int n = Math.Min(keys.Length, counts.Length);
                for (int i = 0; i < n; i++)
                    if (!string.IsNullOrEmpty(keys[i]))
                        _overrides[keys[i]] = counts[i];
            }
        }

        protected override string TitleKey => MOD_PREFIX + "CargoActions_Ammo";

        protected override void OnDismiss()
        {
            if (_onSaved != null)
                _onSaved();
        }

        int GetCount(string subtype)
        {
            int value;
            if (subtype != null && _overrides.TryGetValue(subtype, out value))
                return value;
            return Config.AmmoDefaultPerWeapon;
        }

        void FlushOverridesToConfig()
        {
            var keys = new string[_overrides.Count];
            var counts = new int[_overrides.Count];
            int i = 0;
            foreach (var pair in _overrides)
            {
                keys[i] = pair.Key;
                counts[i] = pair.Value;
                i++;
            }

            Config.WeaponOverrideKeys = keys;
            Config.WeaponOverrideCounts = counts;
        }

        protected override void RenderContent(RectangleF contentRect, float scale, float fontScale,
            IMyTextSurface surface)
        {
            var rowH = 34f * scale;
            var gap = 8f * scale;

            DrawLeftLabel(MyTexts.GetString(MOD_PREFIX + "CargoActions_Default"),
                new Vector2(contentRect.X, contentRect.Y + rowH * 0.5f), contentRect.Width * 0.5f, rowH, ON_SURFACE);
            var defaultRect = new RectangleF(contentRect.X + contentRect.Width * 0.55f, contentRect.Y, contentRect.Width * 0.45f, rowH);
            RenderDefaultInput(defaultRect);

            var selRowY = contentRect.Bottom - rowH;
            var selName = _selected != null ? _selected.DisplayName : string.Empty;
            DrawLeftLabel(selName, new Vector2(contentRect.X, selRowY + rowH * 0.5f), contentRect.Width * 0.5f, rowH, PRIMARY);
            var selRect = new RectangleF(contentRect.X + contentRect.Width * 0.55f, selRowY, contentRect.Width * 0.45f, rowH);
            RenderSelectedInput(selRect);

            var listTop = contentRect.Y + rowH + gap;
            var listHeight = Math.Max(rowH, selRowY - gap - listTop);
            var listRect = new RectangleF(contentRect.X, listTop, contentRect.Width, listHeight);
            RenderList(listRect, 28f * scale);
        }

        void RenderDefaultInput(RectangleF rect)
        {
            if (_defaultInput == null)
            {
                var model = new NumericUpDownModel
                {
                    Value = Config.AmmoDefaultPerWeapon,
                    MinValue = 0,
                    MaxValue = 100000,
                    Step = 1,
                    Format = "0",
                    Title = MyTexts.GetString(MOD_PREFIX + "CargoActions_Default"),
                    ValueChanged = OnDefaultChanged
                };
                _defaultInput = new NumericUpDown(rect, model);
            }
            else
            {
                _defaultInput.SetRect(rect);
                if (_defaultInput.NumericModel != null)
                    _defaultInput.NumericModel.Value = Config.AmmoDefaultPerWeapon;
            }

            _defaultInput.SetVisible(true);
            ContainerControl.AddChild(_defaultInput);
            _defaultInput.Render(Sprites);
        }

        void OnDefaultChanged(double value)
        {
            Config.AmmoDefaultPerWeapon = (int)Math.Round(value);
            Redraw();
        }

        void RenderList(RectangleF rect, float rowHeight)
        {
            if (_listModel == null)
            {
                _listModel = new ListBoxModel<WeaponOption>
                {
                    Items = _options,
                    SelectedEntries = new List<WeaponOption>(),
                    MultiSelect = false,
                    RowHeight = rowHeight,
                    TextSelector = WeaponLabel,
                    EntryClicked = OnWeaponPicked
                };
            }

            _listModel.RowHeight = rowHeight;

            if (_list == null)
                _list = new ListBox<WeaponOption>(rect, _listModel);
            else
                _list.SetRect(rect);

            _list.SetVisible(true);
            ContainerControl.AddChild(_list);
            _list.Render(Sprites);
        }

        string WeaponLabel(WeaponOption option)
        {
            if (option == null)
                return string.Empty;
            return option.DisplayName + "  ·  " + GetCount(option.SubtypeId);
        }

        void OnWeaponPicked(WeaponOption option)
        {
            _selected = option;
            if (_selectedInput != null && _selectedInput.NumericModel != null && option != null)
                _selectedInput.NumericModel.Value = GetCount(option.SubtypeId);
            Redraw();
        }

        void RenderSelectedInput(RectangleF rect)
        {
            double value = _selected != null ? GetCount(_selected.SubtypeId) : Config.AmmoDefaultPerWeapon;

            if (_selectedInput == null)
            {
                var model = new NumericUpDownModel
                {
                    Value = value,
                    MinValue = 0,
                    MaxValue = 100000,
                    Step = 1,
                    Format = "0",
                    Title = MyTexts.GetString(MOD_PREFIX + "CargoActions_Ammo"),
                    Enabled = _selected != null,
                    ValueChanged = OnSelectedChanged
                };
                _selectedInput = new NumericUpDown(rect, model);
            }
            else
            {
                _selectedInput.SetRect(rect);
                if (_selectedInput.NumericModel != null)
                {
                    _selectedInput.NumericModel.Enabled = _selected != null;
                    _selectedInput.NumericModel.Value = value;
                }
            }

            _selectedInput.SetVisible(true);
            ContainerControl.AddChild(_selectedInput);
            _selectedInput.Render(Sprites);
        }

        void OnSelectedChanged(double value)
        {
            if (_selected == null)
                return;

            var rounded = (int)Math.Round(value);
            if (rounded == Config.AmmoDefaultPerWeapon)
                _overrides.Remove(_selected.SubtypeId);
            else
                _overrides[_selected.SubtypeId] = rounded;
            FlushOverridesToConfig();
            Redraw();
        }

        void Redraw()
        {
            if (_requestRedraw != null)
                _requestRedraw();
        }
    }
}
