using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    /// <summary>Modal option list used by compact combo boxes.</summary>
    sealed class ComboBoxSelectionDialog<T> : Dialog
    {
        readonly List<T> _options = new List<T>();
        readonly List<T> _selection = new List<T>();
        readonly Func<T, string> _getLabel;
        readonly Action<T> _selected;
        readonly ListBoxModel<T> _listModel;
        readonly ListBox<T> _listBox;

        public ComboBoxSelectionDialog(IApp parentApp, IEnumerable<T> options, T selected,
            Func<T, string> getLabel, Action<T> selectionChanged)
            : base(parentApp)
        {
            if (options != null)
                _options.AddRange(options);
            if (_options.Contains(selected))
                _selection.Add(selected);

            _getLabel = getLabel ?? (value => value == null ? string.Empty : value.ToString());
            _selected = selectionChanged;
            _listModel = new ListBoxModel<T>
            {
                Items = _options,
                SelectedEntries = _selection,
                MultiSelect = false,
                SelectionEnabled = true,
                TextSelector = _getLabel,
                EntryClicked = OnEntryClicked
            };
            _listBox = new ListBox<T>(default(RectangleF), _listModel);
        }

        protected override void BuildDialogControls(InteractiveSurfaceScript owner, RectangleF viewBox, float scale,
            float fontScale, Sandbox.ModAPI.Ingame.IMyTextSurface surface, Color textColor, Color backgroundColor,
            Color panelColor, Vector2 cursorPosition)
        {
            var container = EnsureContainer(viewBox);
            container.ClearChildren();
            var card = GetDialogCardRect(viewBox, scale, .62f, .72f, 260f, 180f);
            RegisterDialogCard(card);

            Sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", surface.TextureSize / 2f,
                surface.TextureSize, new Color(0, 0, 0, 128)));
            if (!IsTinyDialogAspectRatio(viewBox))
                BorderRenderer.CreateSpritesFromRect(new RectangleF(card.Position + 2f * scale, card.Size), Sprites,
                    ResolveColor(ThemeResources.ShadowColor), radiusScale: scale);
            BorderRenderer.CreateSpritesFromRect(card, Sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighColor), radiusPixels: DialogCardRadiusPixels,
                radiusScale: scale);

            var padding = GetDialogPadding(viewBox, scale);
            var content = GetDialogContentRect(card, viewBox, scale, padding);
            _listModel.RowHeight = Math.Max(28f * scale, Math.Min(42f * scale, content.Height));
            _listModel.ScrollerWidthPixels = Math.Max(5f, 7f * scale);
            _listBox.SetRect(content);
            _listBox.BackgroundColor = ResolveColor(ThemeResources.SurfaceContainerColor);
            _listBox.SetVisible(true);
            container.AddChild(_listBox);
            _listBox.Render(Sprites);
        }

        void OnEntryClicked(T value)
        {
            Dismiss();
            if (_selected != null)
                _selected(value);
        }
    }
}
