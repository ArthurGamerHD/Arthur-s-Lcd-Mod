using System;
using Acrux.Controls;
using Acrux.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using VRageMath;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>Shows windows modally on top of the top-most open window, wiring a DialogViewModel DataContext.</summary>
    internal static class DialogService
    {
        // Toolkit dialog texts use the mod's translations of the same keys ("LcdMod_" + key).
        internal static string LocalizeToolkitText(string key)
        {
            string id = "LcdMod_" + key;
            string text = LocHelper.GetLoc(id);
            return string.Equals(text, id, StringComparison.Ordinal) ? null : text;
        }

        public static Window TopMost(Window root)
        {
            var owner = root;
            while (owner.OwnedWindows.Count > 0)
                owner = owner.OwnedWindows[owner.OwnedWindows.Count - 1];
            return owner;
        }

        // A dialog's look on a surface: its view model's font size and texts (what ShowModal sets first).
        internal static void Prepare(Window dialog, float fontSize)
        {
            var model = dialog.DataContext as DialogViewModel;
            if (model != null)
                model.FontSize = fontSize;
            // Dialogs that Acrux UI provides (e.g. the file picker) use the toolkit's dialog model.
            var shared = dialog.DataContext as Acrux.Dialogs.DialogViewModel;
            if (shared != null)
            {
                shared.FontSize = fontSize;
                if (shared.Localizer == null)
                    shared.Localizer = LocalizeToolkitText;
            }
        }

        public static void ShowModal(Window dialog, Window root, float fontSize)
        {
            Prepare(dialog, fontSize);
            var model = dialog.DataContext as DialogViewModel;
            if (model != null)
            {
                model.CloseRequested = dialog.Close;
                dialog.Closed += () =>
                {
                    model.CloseRequested = null;
                    model.OnClosed();
                };
            }
            var shared = dialog.DataContext as Acrux.Dialogs.DialogViewModel;
            if (shared != null)
            {
                shared.CloseRequested = dialog.Close;
                dialog.Closed += () =>
                {
                    shared.CloseRequested = null;
                    shared.OnClosed();
                };
            }
            Window owner = TopMost(root);
            CompactLayout.Follow(dialog, owner);
            dialog.ShowModal(owner);
        }

        /// <summary>
        /// On very short screens (e.g. 512x100) a dialog fills the whole screen without a title bar, outer margin or
        /// rounded frame, keeping only its close button beside the content, like the pre-migration compact dialogs.
        /// It follows the owner's size, so resizing the screen switches between the two layouts.
        /// </summary>
        internal sealed class CompactLayout
        {
            const float Padding = 4f;
            const float CaptionGutter = 32f;

            readonly Window _dialog;
            bool _compact;
            WindowState _state;
            bool _extendClientArea;
            bool _canMaximize;
            bool _canMinimize;
            Vector4 _padding;

            CompactLayout(Window dialog)
            {
                _dialog = dialog;
            }

            internal bool IsCompact => _compact;

            // The layout switch alone; the caller applies it with the size the dialog is shown on.
            internal static CompactLayout For(Window dialog)
            {
                return new CompactLayout(dialog);
            }

            internal static CompactLayout Follow(Window dialog, Window owner)
            {
                var layout = new CompactLayout(dialog);
                Action<object, SizeChangedEventArgs> resized = (_, e) => layout.Apply(e.NewSize);
                owner.SizeChanged += resized;
                dialog.Closed += () => owner.SizeChanged -= resized;
                layout.Apply(owner.Bounds.Size);
                return layout;
            }

            internal static bool IsTiny(Vector2 size)
            {
                return size.X > 0f && size.Y > 0f && size.Y / size.X < Constants.MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO;
            }

            internal void Apply(Vector2 ownerSize)
            {
                if (ownerSize.X <= 0f || ownerSize.Y <= 0f)
                    return;
                bool tiny = IsTiny(ownerSize);
                if (tiny == _compact)
                    return;
                _compact = tiny;
                var model = _dialog.DataContext as DialogViewModel;
                if (model != null)
                    model.IsCompact = tiny;
                var shared = _dialog.DataContext as Acrux.Dialogs.DialogViewModel;
                if (shared != null)
                    shared.IsCompact = tiny;
                if (tiny)
                {
                    _state = _dialog.WindowState;
                    _extendClientArea = _dialog.ExtendClientAreaToDecorationsHint;
                    _canMaximize = _dialog.CanMaximize;
                    _canMinimize = _dialog.CanMinimize;
                    _padding = _dialog.Padding;
                    _dialog.WindowState = WindowState.Maximized;
                    _dialog.ExtendClientAreaToDecorationsHint = true;
                    _dialog.CanMaximize = false;
                    _dialog.CanMinimize = false;
                    _dialog.Padding = WindowPlatforms.CaptionButtons == CaptionButtonsPlacement.Left
                        ? new Vector4(CaptionGutter, Padding, Padding, Padding)
                        : new Vector4(Padding, Padding, CaptionGutter, Padding);
                    return;
                }
                _dialog.WindowState = _state;
                _dialog.ExtendClientAreaToDecorationsHint = _extendClientArea;
                _dialog.CanMaximize = _canMaximize;
                _dialog.CanMinimize = _canMinimize;
                _dialog.Padding = _padding;
            }
        }
    }
}
