using System;
using Adk.Gui.Core.Input;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>Shared state of a modal dialog's DataContext: title, font size and the request to close.</summary>
    internal abstract class DialogViewModel : ObservableObject
    {
        internal static Func<string, string> Localize = key => key;

        string _title = string.Empty;
        float _fontSize = .65f;
        bool _isCompact;

        protected DialogViewModel()
        {
            CloseCommand = new DelegateCommand(_ => Close());
        }

        public string Title
        {
            get { return _title; }
            set { RaiseAndSetIfChanged(ref _title, value ?? string.Empty, nameof(Title)); }
        }

        public float FontSize
        {
            get { return _fontSize; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _fontSize, value, nameof(FontSize)))
                    return;
                RaisePropertyChanged<float>(nameof(SmallFontSize));
                OnFontSizeChanged();
            }
        }

        public float SmallFontSize => _fontSize * .85f;

        /// <summary>True while the dialog fills a very short screen, where views lay their actions out beside the content.</summary>
        public bool IsCompact
        {
            get { return _isCompact; }
            set
            {
                if (RaiseAndSetIfChanged(ref _isCompact, value, nameof(IsCompact)))
                    RaisePropertyChanged<bool>(nameof(IsRegular));
            }
        }

        public bool IsRegular => !_isCompact;
        public ICommand CloseCommand { get; }
        public Action CloseRequested { get; set; }

        public void Close()
        {
            var close = CloseRequested;
            if (close != null)
                close();
        }

        protected virtual void OnFontSizeChanged()
        {
        }

        protected internal virtual void OnClosed()
        {
        }

        protected static string Loc(string key)
        {
            return Localize(key);
        }

        protected static string Loc(string key, object argument)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, Localize(key), argument);
        }
    }
}
