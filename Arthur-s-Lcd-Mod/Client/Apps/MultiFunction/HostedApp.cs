using System;
using Acrux.Core.Input;
using Adk.Mvvm;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Common.Config.Components;
using VRageMath;

namespace LcdMod.Client.Apps.MultiFunction
{
    /// <summary>
    /// An app the display opened: its config, the app, its window, and its taskbar button and page dot.
    /// </summary>
    internal sealed class HostedApp : ObservableObject
    {
        bool _isActive;
        bool _isMinimized;
        bool _isCurrent;

        public HostedApp(AppInstanceConfig config, MultiFunctionAppInfo info, HostedAppHost host, App app,
            Action<HostedApp> task, Action<HostedApp> select)
        {
            Config = config;
            Info = info;
            Host = host;
            App = app;
            Title = info.Name;
            Icon = info.Icon;
            TaskCommand = new DelegateCommand(_ => task(this));
            SelectCommand = new DelegateCommand(_ => select(this));
        }

        public AppInstanceConfig Config { get; private set; }
        public MultiFunctionAppInfo Info { get; }
        public HostedAppHost Host { get; }
        public App App { get; }
        public HostedAppView Window { get; set; }

        internal bool IsMaximized { get; set; }

        internal void Rebind(AppInstanceConfig config)
        {
            Config = config;
            Host.SetConfig(config);
        }

        /// <summary>The area the app was last laid out for, until it settles (see the display's UpdateViewBox).</summary>
        internal RectangleF? PendingViewBox { get; set; }
        internal long PendingSince { get; set; }

        public string Title { get; }
        public string Icon { get; }
        public ICommand TaskCommand { get; }
        public ICommand SelectCommand { get; }

        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (RaiseAndSetIfChanged(ref _isActive, value, nameof(IsActive)))
                    RaisePropertyChanged<string>(nameof(TaskClasses));
            }
        }

        public bool IsMinimized
        {
            get { return _isMinimized; }
            set
            {
                if (RaiseAndSetIfChanged(ref _isMinimized, value, nameof(IsMinimized)))
                    RaisePropertyChanged<string>(nameof(TaskClasses));
            }
        }

        public bool IsCurrent
        {
            get { return _isCurrent; }
            set
            {
                RaiseAndSetIfChanged(ref _isCurrent, value, nameof(IsCurrent));
            }
        }

        public string TaskClasses => "mfd-bar-button mfd-task" + (_isActive ? " active" : string.Empty) +
                                     (_isMinimized ? " minimized" : string.Empty);
    }
}
