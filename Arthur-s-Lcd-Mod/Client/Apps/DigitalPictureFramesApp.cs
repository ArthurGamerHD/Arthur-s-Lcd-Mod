using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Apps
{
    internal sealed class DigitalPictureFramesApp : AppBase, IAppInteractive
    {
        const float BUTTON_WIDTH_PIXELS = 220f;
        const float BUTTON_HEIGHT_PIXELS = 42f;

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly Button _pickBackgroundButton;
        readonly RectangleControl _imagePickerHitbox;
        readonly InteractiveSurfaceScript _interactiveHost;

        new ScreenConfigDigitalPictureFrames AppConfig => (ScreenConfigDigitalPictureFrames)base.AppConfig;

        public DigitalPictureFramesApp(ScreenConfigDigitalPictureFrames config, InteractiveSurfaceScript host)
            : base(config, host)
        {
            _interactiveHost = host;

            _pickBackgroundButton = new Button(default(RectangleF), new ButtonModel
            {
                Text = "Pick Background",
                Clicked = OnPickBackgroundClicked
            });
            _imagePickerHitbox = new RectangleControl(default(RectangleF), CursorType.Hand, null, OnImageClicked)
            {
                CustomRender = RenderImagePickerHitbox
            };
            _interactiveList.Add(_pickBackgroundButton);
            _interactiveList.Add(_imagePickerHitbox);
        }

        public List<ControlBase> InteractiveList => _interactiveList;

        public override void Update()
        {
            UpdateControls();
        }

        public bool HasVisibleItems()
        {
            return true;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();

            var config = AppConfig;
            if (config == null)
                return _sprites;

            Host.AddBackground(_sprites);
            Host.DrawTitle(_sprites);
            DrawBackgroundImage(config.BackgroundSprite);

            return _sprites;
        }

        void DrawBackgroundImage(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return;

            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = spriteName,
                Position = Host.ViewBox.Center,
                Size = Host.ViewBox.Size,
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        void UpdateControls()
        {
            var config = AppConfig;
            if (config == null)
            {
                _pickBackgroundButton.SetVisible(false);
                _imagePickerHitbox.SetVisible(false);
                return;
            }

            var hasBackground = !string.IsNullOrWhiteSpace(config.BackgroundSprite);
            var scale = Math.Max(0.75f, Host.Scale);
            var width = Math.Min(BUTTON_WIDTH_PIXELS * scale, Math.Max(1f, Host.ViewBox.Width * 0.5f));
            var height = Math.Max(1f, BUTTON_HEIGHT_PIXELS * scale);

            _pickBackgroundButton.SetRect(new RectangleF(
                Host.ViewBox.Center.X - width * 0.5f,
                Host.ViewBox.Center.Y - height * 0.5f,
                width,
                height));
            _pickBackgroundButton.SetVisible(!hasBackground);

            _imagePickerHitbox.SetRect(Host.ViewBox);
            _imagePickerHitbox.SetCursor(CursorType.Hand);
            _imagePickerHitbox.SetVisible(hasBackground);
            _imagePickerHitbox.CustomRender = RenderImagePickerHitbox;

            var model = _pickBackgroundButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = "Pick Background";
                model.Enabled = true;
                model.Clicked = OnPickBackgroundClicked;
            }

            _pickBackgroundButton.SetStyle(Button.CreatePrimaryButtonStyle(Theme));
        }

        void OnPickBackgroundClicked(ButtonModel model, object sender)
        {
            if (_interactiveHost == null)
                return;

            _interactiveHost.ShowDialog(new SpritePicker(this, OnSpriteSelected, _interactiveHost.RequestRedraw));
        }

        void OnImageClicked(object dataContext, object sender)
        {
            OnPickBackgroundClicked(null, sender);
        }

        static void RenderImagePickerHitbox(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
        }

        void OnSpriteSelected(string spriteName)
        {
            var config = AppConfig;
            if (config == null)
                return;

            var normalized = string.IsNullOrWhiteSpace(spriteName)
                ? string.Empty
                : spriteName.Trim();

            if (string.Equals(config.BackgroundSprite, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            config.BackgroundSprite = normalized;

            if (_interactiveHost != null && _interactiveHost.Block != null && _interactiveHost.ProviderConfig != null)
                ConfigManager.Sync(_interactiveHost.Block, _interactiveHost.ProviderConfig);
        }
    }
}
