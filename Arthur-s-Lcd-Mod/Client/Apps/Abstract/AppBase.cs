using System;
using System.Collections.Generic;
using System.ComponentModel;
using Adk.Gui.Animation;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.GridData;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Apps.Abstract
{
    [ConfigComponent(Constants.GENERAL, typeof(GeneralConfigComponent), PropertyName = "GeneralComponent")]
    [ConfigComponent(Constants.COLORS, typeof(ColorConfigComponent), PropertyName = "ColorComponent")]
    [ConfigComponent(Constants.INTERACTION, typeof(InteractiveConfigComponent), PropertyName = "InteractionComponent")]
    public abstract partial class App : IApp, ITextSurfaceProvider, ITextStyleProvider,
        INotifyPropertyChanged, Adk.Gui.Styling.IResourceProvider
    {
        AnimationController _animationController;
        ResourceTree _resources;
        bool _isDirty;
        Dictionary<string, Color> _theme;
        Color _themeHeaderColor;
        Color _themeForegroundColor;
        Color _themeWarningColor;
        Color _themeErrorColor;
        bool _themeDark;
        float _themeLayoutScale;
        float _themeFontScale;
        float _themeAutoScrollSecondsPerStep;
        string _themeTextFont;
        bool _hasTheme;
        GridLogic _neededGridLogic;
        GridCapability _gridCapabilities;
        Adk.Gui.Core.VisualElement _content;
        Adk.Gui.Core.VisualElement _footer;
        Adk.Gui.Core.VisualElement _backgroundContent;
        int _contentRow = 1;
        int _rowSpan = 1;
        RectangleF _retainedContentBounds;
        bool _hasRetainedContentBounds;
        readonly List<Adk.Gui.Controls.Window> _dialogs = new List<Adk.Gui.Controls.Window>();

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string> ResourceChanged;

        public virtual string Title => Host.Title;
        public float SurfaceFontSize => GetFontScaleResourceValue();
        public string TitleIcon => Host.TitleIcon;
        public bool TitleEnabled => Host.TitleVisible;
        public Adk.Gui.Core.VisualElement Content => _content;
        // Legacy sprite apps already bake their scale into their coordinates.
        // Migrated logical-unit views opt in to the window's DPI scale.
        internal virtual float WindowRenderScaling => 1f;
        internal Color ThemeColor(ResourceKey<Color> key)
        {
            Color color;
            return Resources.TryGet(key, out color) ? color : Color.White;
        }

        internal float CardScale => GeneralComponent.GetScale() > 0f ? GeneralComponent.GetScale() : 1f;
        internal bool IsGridDisplay => GeneralComponent.DisplayMode == (int)LcdMod.Client.Terminal.Controls.DisplayMode.Grid;
        public Adk.Gui.Core.VisualElement Footer => _footer;

        /// <summary>Full-surface layer drawn beneath the title and the content, e.g. a scene the title overlays.</summary>
        public Adk.Gui.Core.VisualElement BackgroundContent => _backgroundContent;
        public int ContentRow => _contentRow;
        public int RowSpan => _rowSpan;
        public bool ChromeVisible => TitleEnabled && (_contentRow != 0 || _rowSpan != 3);
        public bool FooterVisible => _footer != null;
        protected RectangleF RetainedContentBounds =>
            _hasRetainedContentBounds ? _retainedContentBounds : Host.ViewBox;

        protected App(IAppHost host)
        {
            Host = host;
            if (Host == null)
                throw new ArgumentNullException(nameof(host));

            _animationController = Host.Animations;
        }

        protected IAppHost Host { get; private set; }

        /// <summary>Plays a sound on the surface's block, when the surface can play sounds.</summary>
        internal void PlaySound(Sandbox.Game.Entities.MySoundPair sound)
        {
            var interactive = Host as SurfaceScripts.Abstract.InteractiveSurfaceScript;
            if (interactive != null)
                interactive.PlaySounds(sound);
        }
        protected IComponentContainer Config => Host.Config;
        internal AnimationController AnimationController => _animationController;

        public IVisualStyleScope StyleParent => null;

        public virtual void MarkDirty()
        {
            _isDirty = true;
        }

        protected void SetContent(Adk.Gui.Core.VisualElement content)
        {
            if (ReferenceEquals(_content, content))
                return;
            _content = content;
            RaisePropertyChanged(nameof(Content));
        }

        protected void SetBackgroundContent(Adk.Gui.Core.VisualElement content)
        {
            if (ReferenceEquals(_backgroundContent, content))
                return;
            _backgroundContent = content;
            RaisePropertyChanged(nameof(BackgroundContent));
        }

        protected void SetFooter(Adk.Gui.Core.VisualElement footer)
        {
            if (ReferenceEquals(_footer, footer))
                return;
            _footer = footer;
            RaisePropertyChanged(nameof(Footer));
            RaisePropertyChanged(nameof(FooterVisible));
            SetContentLayout(!Host.TitleVisible);
        }

        protected void SetContentLayout(bool occupyAllRows)
        {
            int contentRow = occupyAllRows ? 0 : 1;
            int rowSpan = occupyAllRows ? (_footer == null ? 3 : 2) : 1;
            if (_contentRow == contentRow && _rowSpan == rowSpan)
                return;
            _contentRow = contentRow;
            _rowSpan = rowSpan;
            RaisePropertyChanged(nameof(ContentRow));
            RaisePropertyChanged(nameof(RowSpan));
            RaisePropertyChanged(nameof(ChromeVisible));
        }

        internal void SetRetainedContentBounds(RectangleF bounds)
        {
            if (_hasRetainedContentBounds &&
                Math.Abs(_retainedContentBounds.X - bounds.X) < 0.001f &&
                Math.Abs(_retainedContentBounds.Y - bounds.Y) < 0.001f &&
                Math.Abs(_retainedContentBounds.Width - bounds.Width) < 0.001f &&
                Math.Abs(_retainedContentBounds.Height - bounds.Height) < 0.001f)
                return;

            _retainedContentBounds = bounds;
            _hasRetainedContentBounds = true;
            OnRetainedContentBoundsChanged();
        }

        protected virtual void OnRetainedContentBoundsChanged()
        {
            MarkDirty();
        }

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handlers = PropertyChanged;
            if (handlers != null)
                handlers(this, new PropertyChangedEventArgs(propertyName));
        }

        public bool TryGetResource<T>(string key, out T value)
        {
            if (string.Equals(key, "factionIcon", StringComparison.Ordinal) &&
                typeof(T) == typeof(string))
            {
                value = (T)(object)TitleIcon;
                return true;
            }
            if (string.Equals(key, "factionIconMask", StringComparison.Ordinal) &&
                MyAPIGateway.Reflection.IsAssignableFrom(typeof(T), typeof(Adk.Gui.Styling.ImageBrush)))
            {
                value = (T)(object)new Adk.Gui.Styling.ImageBrush(TitleIcon) { Stretch = Adk.Gui.Styling.Stretch.Uniform };
                return true;
            }

            // Building the theme first also registers the ThemeResources keys, which the lookup below needs.
            EnsureResources();
            ResourceKey<T> resourceKey;
            if (ResourceKey.TryGet(key, out resourceKey))
                return _resources.TryGet(resourceKey, out value);
            value = default(T);
            return false;
        }

        protected void RaiseResourceChanged(string key = null)
        {
            Action<string> handlers = ResourceChanged;
            if (handlers != null)
                handlers(key);
        }

        protected void RebindAppHost(IAppHost host)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            Host = host;
            _animationController = Host.Animations;
        }

        protected void NeedGridData(GridCapability need)
        {
            var logic = Host != null ? Host.GridLogic : null;
            if (!ReferenceEquals(_neededGridLogic, logic))
                ReleaseGridDataNeeds();
            if (logic == null)
                return;

            var additionalNeeds = need & ~_gridCapabilities;
            if (additionalNeeds == GridCapability.None)
                return;

            logic.RequestCapability(additionalNeeds);
            _neededGridLogic = logic;
            _gridCapabilities |= additionalNeeds;
        }

        void ReleaseGridDataNeeds()
        {
            if (_neededGridLogic != null && _gridCapabilities != GridCapability.None)
                _neededGridLogic.Release(_gridCapabilities);
            _neededGridLogic = null;
            _gridCapabilities = GridCapability.None;
        }

        /// <summary>What makes <see cref="IsDirty"/> true, for render diagnostics.</summary>
        internal string DescribeDirtySource()
        {
            if (_isDirty)
                return "app dirty flag";
            return Adk.Gui.Core.VisualTree.DescribeInvalidation(_content) ??
                   Adk.Gui.Core.VisualTree.DescribeInvalidation(_footer) ??
                   Adk.Gui.Core.VisualTree.DescribeInvalidation(_backgroundContent) ?? "clean";
        }

        public virtual bool IsDirty
        {
            get
            {
                if (_isDirty)
                    return true;

                if (_content != null && _content.Visible &&
                    _content.PendingInvalidation != Adk.Gui.Core.Invalidation.UiInvalidation.None)
                    return true;
                if (_footer != null && _footer.Visible &&
                    _footer.PendingInvalidation != Adk.Gui.Core.Invalidation.UiInvalidation.None)
                    return true;
                return _backgroundContent != null && _backgroundContent.Visible &&
                       _backgroundContent.PendingInvalidation != Adk.Gui.Core.Invalidation.UiInvalidation.None;
            }
        }

        public ResourceTree Resources
        {
            get
            {
                EnsureResources();
                return _resources;
            }
        }

        protected void ClearDirtyAfterRender()
        {
            _isDirty = false;
        }

        internal void CompleteRetainedRender()
        {
            ClearDirtyAfterRender();
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface TextSurface => Host.Surface;

        public string TextFont
        {
            get
            {
                string value;
                return ScopedResourceResolver.TryResolve(this, ThemeResources.TextFont, out value) &&
                       !string.IsNullOrEmpty(value)
                    ? value
                    : "White";
            }
        }

        string ITextStyleProvider.ResolvedTextFont => TextFont;

        protected Vector2 MeasureText(string text, float scale)
        {
            return FormatingHelper.GetSizeInPixel(text, this, scale, TextSurface);
        }

        protected float MeasureLineHeight(float scale, string probe = "Ag")
        {
            return FormatingHelper.LineHeight(scale, this, TextSurface, probe);
        }


        public abstract void Update();

        public virtual void LayoutChanged()
        {
            SetContentLayout(!Host.TitleVisible);
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(TitleIcon));
            RaisePropertyChanged(nameof(TitleEnabled));
            RaisePropertyChanged(nameof(SurfaceFontSize));
            RaiseResourceChanged();
        }

        public abstract List<MySprite> GetSprites();
        

        public virtual bool HasVisibleItems()
        {
            return true;
        }

        public virtual void OnMouseScroll(int delta, ref bool handled)
        {
        }

        public virtual void Close()
        {
            CloseDialogs();
            ReleaseGridDataNeeds();
        }

        internal bool HasOpenDialog => _dialogs.Count > 0;

        /// <summary>Shows a message with one to three buttons; each closes the box, then runs its action.</summary>
        internal bool ShowMessage(string title, string message, params Dialogs.DialogButton[] buttons)
        {
            return ShowDialog(new Dialogs.MessageBoxView(new Dialogs.MessageBoxViewModel(title, message, buttons)));
        }

        /// <summary>Asks for one line of text; <paramref name="confirmed"/> receives it when the user confirms.</summary>
        internal bool AskText(string title, string prompt, string initialText, Action<string> confirmed, string confirmText = null)
        {
            return ShowDialog(new Dialogs.TextPromptView(
                new Dialogs.TextPromptViewModel(title, prompt, initialText, confirmed, confirmText)));
        }

        /// <summary>Opens a dialog above this app (or above its top-most dialog); it is disposed when it closes.</summary>
        internal bool ShowDialog(Adk.Gui.Controls.Window dialog)
        {
            var script = Host as SurfaceScripts.Abstract.SurfaceScriptBase;
            var root = script == null || script.RetainedAppGuiHost == null
                ? null
                : script.RetainedAppGuiHost.Root as Adk.Gui.Controls.Window;
            if (root == null || !root.IsOpen)
            {
                dialog.Dispose();
                return false;
            }

            dialog.Closed += () =>
            {
                _dialogs.Remove(dialog);
                dialog.Dispose();
                Host.RenderSprites();
            };
            _dialogs.Add(dialog);
            try
            {
                Dialogs.DialogService.ShowModal(dialog, root, .65f * SurfaceFontSize);
            }
            catch
            {
                _dialogs.Remove(dialog);
                dialog.Dispose();
                throw;
            }
            Host.RenderSprites();
            return true;
        }

        void CloseDialogs()
        {
            for (int i = _dialogs.Count - 1; i >= 0; i--)
                if (i < _dialogs.Count)
                    _dialogs[i].Close();
        }

        protected Color GetHeaderColor()
        {
            return ColorComponent.ResolveHeaderColor(Host.Block as Sandbox.ModAPI.IMyTerminalBlock);
        }

        protected Color GetForegroundColor()
        {
            return Host.ForegroundColor;
        }

        protected Color ResolveResource(ResourceKey<Color> key, Color fallback)
        {
            Color value;
            return ScopedResourceResolver.TryResolve(this, key, out value) ? value : fallback;
        }

        protected Color ResolveRole(string role, Color fallback)
        {
            return ResolveResource(ThemeResources.FromThemeRole(role), fallback);
        }

        void EnsureResources()
        {
            var headerColor = GetHeaderColor();
            var foregroundColor = GetForegroundColor();
            var warningColor = ColorComponent.ResolveWarningColor();
            var errorColor = ColorComponent.ResolveErrorColor();
            bool dark = ShouldUseDarkTheme();
            float layoutScale = GetLayoutScaleResourceValue();
            float fontScale = GetFontScaleResourceValue();
            string textFont = GetTextFontResourceValue();

            if (!_hasTheme ||
                !headerColor.Equals(_themeHeaderColor) ||
                !foregroundColor.Equals(_themeForegroundColor) ||
                !warningColor.Equals(_themeWarningColor) ||
                !errorColor.Equals(_themeErrorColor) ||
                dark != _themeDark ||
                !layoutScale.Equals(_themeLayoutScale) ||
                !fontScale.Equals(_themeFontScale) ||
                !InteractionComponent.AutoScrollStep.Equals(_themeAutoScrollSecondsPerStep) ||
                !string.Equals(textFont, _themeTextFont, StringComparison.Ordinal))
            {
                _theme = headerColor.ToTheme(dark);
                _resources = ThemeResourceBuilder.FromThemeDictionary(_theme);
                _resources.Set(ThemeResources.FontColor, foregroundColor);
                _resources.Set(ThemeResources.HeaderColor, headerColor);
                _resources.Set(ThemeResources.CardShadowColor, headerColor.MulValue(.2f));
                Color accent;
                if (_resources.TryGet(ThemeResources.AccentColor, out accent))
                    _resources.Set(ThemeResources.CardFillColor, accent.EnsureMinimalContrast(headerColor));
                _resources.Set(ThemeResources.WarningColor, warningColor);
                _resources.Set(ThemeResources.ErrorColor, errorColor);
                _resources.Set(ThemeResources.LayoutScale, layoutScale);
                _resources.Set(ThemeResources.FontScale, fontScale);
                _resources.Set(ThemeResources.AutoScrollSecondsPerStep, InteractionComponent.AutoScrollStep);
                _resources.Set(ThemeResources.TextFont, textFont);
                _themeHeaderColor = headerColor;
                _themeForegroundColor = foregroundColor;
                _themeWarningColor = warningColor;
                _themeErrorColor = errorColor;
                _themeDark = dark;
                _themeLayoutScale = layoutScale;
                _themeFontScale = fontScale;
                _themeAutoScrollSecondsPerStep = InteractionComponent.AutoScrollStep;
                _themeTextFont = textFont;
                _hasTheme = true;
                MarkDirty();
            }
        }

        float GetLayoutScaleResourceValue()
        {
            float scale = GeneralComponent.GetScale();
            return scale > 0f ? scale : 1f;
        }

        float GetFontScaleResourceValue()
        {
            float fontScale = Host.Surface.FontSize;
            return fontScale > 0f ? fontScale : 1f;
        }

        string GetTextFontResourceValue()
        {
            string font = Host.Surface.Font;
            return string.IsNullOrEmpty(font) ? "White" : font;
        }

        bool ShouldUseDarkTheme()
        {
            var backgroundColor = Host.ForegroundColor;
            return backgroundColor.ContrastRatio(Color.Black) >=
                   backgroundColor.ContrastRatio(Color.White);
        }

    }
}
