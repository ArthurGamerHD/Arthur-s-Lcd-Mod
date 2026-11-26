using System;
using System.Collections.Generic;
using Acrux.Core;
using Acrux.Core.Input;
using Acrux.Styling;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.MultiFunction
{
    /// <summary>What the multi-function display's bars, start menu and desktop show.</summary>
    [PreviewWith("IsPagesMode", true)]
    [PreviewWith("IsHorizontal", true)]
    [PreviewWith("CanSwitchPage", true)]
    [PreviewWith("HasSettingsTarget", true)]
    [PreviewWith("CurrentPageIndex", 0)]
    [PreviewWith("ClockText", "12:30")]
    [PreviewWith("StartMenuTitle", "Applications")]
    [PreviewWith("OpenApps", 3)]
    [PreviewWith("OpenApps.Title", "Energy Dashboard", "Thrust", "Inventory")]
    [PreviewWith("OpenApps.Icon", "IconEnergy", "MissingIcon", "Battery")]
    [PreviewWith("OpenApps.TaskClasses", "mfd-bar-button mfd-task active", "mfd-bar-button mfd-task", "mfd-bar-button mfd-task minimized")]
    [PreviewWith("StartEntries", 4)]
    [PreviewWith("StartEntries.Name", "Energy Dashboard", "Thrust", "Inventory", "Gas")]
    [PreviewWith("StartEntries.Icon", "IconEnergy", "MissingIcon", "Battery", "IconHydrogen")]
    internal sealed class MultiFunctionViewModel : ObservableObject
    {
        readonly ObservableList<HostedApp> _openApps = new ObservableList<HostedApp>();
        IReadOnlyList<StartMenuEntry> _startEntries = new StartMenuEntry[0];
        bool _isPagesMode = true;
        bool _isHorizontal = true;
        bool _isStartMenuOpen;
        bool _canSwitchPage;
        bool _hasSettingsTarget;
        string _clockText = string.Empty;
        string _startMenuTitle = string.Empty;
        int _currentPageIndex = -1;
        readonly Action<int> _selectPage;

        public MultiFunctionViewModel(Action start, Action previous, Action next, Action settings, Action<int> selectPage)
        {
            _selectPage = selectPage;
            StartCommand = new DelegateCommand(_ => start());
            PreviousPageCommand = new DelegateCommand(_ => previous());
            NextPageCommand = new DelegateCommand(_ => next());
            SettingsCommand = new DelegateCommand(_ => settings());
        }

        public ICommand StartCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand SettingsCommand { get; }

        public ObservableList<HostedApp> OpenApps => _openApps;
        internal ObservableList<HostedApp> OpenAppList => _openApps;

        public IReadOnlyList<StartMenuEntry> StartEntries
        {
            get { return _startEntries; }
            set { RaiseAndSetIfChanged(ref _startEntries, value, nameof(StartEntries)); }
        }


        public bool IsPagesMode
        {
            get { return _isPagesMode; }
            set
            {
                if (RaiseAndSetIfChanged(ref _isPagesMode, value, nameof(IsPagesMode)))
                    RaisePropertyChanged<bool>(nameof(IsWindowsMode));
            }
        }

        public bool IsWindowsMode => !_isPagesMode;

        public bool IsHorizontal
        {
            get { return _isHorizontal; }
            set
            {
                if (RaiseAndSetIfChanged(ref _isHorizontal, value, nameof(IsHorizontal)))
                    RaisePropertyChanged<bool>(nameof(IsVertical));
            }
        }

        public bool IsVertical => !_isHorizontal;

        public bool IsStartMenuOpen
        {
            get { return _isStartMenuOpen; }
            set
            {
                if (RaiseAndSetIfChanged(ref _isStartMenuOpen, value, nameof(IsStartMenuOpen)))
                    RaisePropertyChanged<string>(nameof(StartClasses));
            }
        }

        public string StartClasses => _isStartMenuOpen ? "mfd-bar-button mfd-start open" : "mfd-bar-button mfd-start";

        public bool CanSwitchPage
        {
            get { return _canSwitchPage; }
            set { RaiseAndSetIfChanged(ref _canSwitchPage, value, nameof(CanSwitchPage)); }
        }

        public bool HasSettingsTarget
        {
            get { return _hasSettingsTarget; }
            set { RaiseAndSetIfChanged(ref _hasSettingsTarget, value, nameof(HasSettingsTarget)); }
        }

        public bool HasPages => _openApps.Count > 0;

        internal void RaisePagesChanged()
        {
            RaisePropertyChanged<bool>(nameof(HasPages));
        }

        public string ClockText
        {
            get { return _clockText; }
            set { RaiseAndSetIfChanged(ref _clockText, value ?? string.Empty, nameof(ClockText)); }
        }

        public string StartMenuTitle
        {
            get { return _startMenuTitle; }
            set { RaiseAndSetIfChanged(ref _startMenuTitle, value ?? string.Empty, nameof(StartMenuTitle)); }
        }

        public int CurrentPageIndex
        {
            get { return _currentPageIndex; }
            set
            {
                if (value == _currentPageIndex || value < 0 || value >= _openApps.Count)
                    return;
                _currentPageIndex = value;
                RaisePropertyChanged<int>(nameof(CurrentPageIndex));
                if (_selectPage != null)
                    _selectPage(value);
            }
        }

        internal void SetCurrentPageIndex(int index)
        {
            RaiseAndSetIfChanged(ref _currentPageIndex, index, nameof(CurrentPageIndex));
        }
    }

    /// <summary>One app of the start menu.</summary>
    internal sealed class StartMenuEntry : ObservableObject
    {
        public StartMenuEntry(MultiFunctionAppInfo info, Action<MultiFunctionAppInfo> open)
        {
            Info = info;
            Name = info.Name;
            Icon = info.Icon;
            OpenCommand = new DelegateCommand(_ => open(info));
        }

        public MultiFunctionAppInfo Info { get; }
        public string Name { get; }
        public string Icon { get; }
        public ICommand OpenCommand { get; }
    }

    /// <summary>The settings window's content: the hosted app's terminal controls.</summary>
    [PreviewWith("Title", "Energy Dashboard settings")]
    [PreviewWith("EmptyText", "This application has no settings to change here.")]
    [PreviewWith("CloseAppText", "Close application")]
    internal sealed class AppSettingsViewModel : ObservableObject
    {
        IReadOnlyList<TerminalSettingRow> _rows = new TerminalSettingRow[0];

        public AppSettingsViewModel(string title, string emptyText, string closeAppText, Action closeApp)
        {
            Title = title;
            EmptyText = emptyText;
            CloseAppText = closeAppText;
            CloseAppCommand = new DelegateCommand(_ => closeApp());
        }

        public string Title { get; }

        public IReadOnlyList<TerminalSettingRow> Rows
        {
            get { return _rows; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _rows, value, nameof(Rows)))
                    return;
                RaisePropertyChanged<bool>(nameof(IsEmpty));
            }
        }

        public bool IsEmpty => _rows.Count == 0;
        public string EmptyText { get; }
        public string CloseAppText { get; }
        public ICommand CloseAppCommand { get; }
    }
}
