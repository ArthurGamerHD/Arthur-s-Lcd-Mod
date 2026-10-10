#if DEBUG
using System;
using LcdMod.Client.Modules.EyeTracking;
using LcdMod.Client.SurfaceScripts.Abstract;

namespace LcdMod.Client.Debugging
{
    internal sealed class VisibleTreeDebugService : IDisposable
    {
        readonly VisibleTreeDebugViewModel _viewModel = new VisibleTreeDebugViewModel();
        readonly VisibleTreeDebugWindow _window;
        SurfaceScriptBase _target;

        public VisibleTreeDebugService()
        {
            _window = new VisibleTreeDebugWindow { DataContext = _viewModel };
            _window.Opened += HandleOpened;
        }

        public bool IsOpen => _window.IsOpen;

        public void Open()
        {
            CaptureTarget();
            _window.Open();
            Refresh();
            LcdMod.Common.Helpers.LogHelper.LogInfo("[VisibleTreeDebug] open requested: target=" +
                (_target == null ? "none" : _target.GetType().Name) + ", window open=" + _window.IsOpen);
        }

        public void Close() { _window.Close(); }

        public void Update()
        {
            if (_window.IsOpen)
                Refresh();
        }

        void HandleOpened()
        {
            CaptureTarget();
            Refresh();
        }

        void CaptureTarget()
        {
            _target = EyeTrackingModule.CurrentPointerSurface ?? EyeTrackingModule.LastPointerSurface;
        }

        void Refresh()
        {
            _viewModel.Refresh(VisibleTreeSnapshot.Build(_target));
        }

        public void Dispose()
        {
            _window.Opened -= HandleOpened;
            _window.Dispose();
        }
    }
}
#endif
