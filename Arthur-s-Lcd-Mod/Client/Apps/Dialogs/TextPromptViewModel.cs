using Adk.Mvvm;
using System;
using Acrux.Core.Input;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>Asks for one line of text, typed through the LCD text box.</summary>
    [PreviewWith("Title", "Save playlist")]
    [PreviewWith("SmallFontSize", 0.5525f)]
    [PreviewWith("IsRegular", true)]
    [PreviewWith("IsCompact", false)]
    [PreviewWith("FontSize", 0.65f)]
    internal sealed class TextPromptViewModel : DialogViewModel
    {
        string _text;

        public TextPromptViewModel(string title, string prompt, string initialText, Action<string> confirmed,
            string confirmText = null)
        {
            Title = title;
            Prompt = prompt ?? string.Empty;
            _text = initialText ?? string.Empty;
            ConfirmText = string.IsNullOrEmpty(confirmText) ? Loc("LcdMod_Common_Button_OK") : confirmText;
            ConfirmCommand = new DelegateCommand(_ =>
            {
                Close();
                if (confirmed != null)
                    confirmed(Text);
            });
        }

        [PreviewWith("Enter a new name for the playlist.")]
        public string Prompt { get; }
        [PreviewWith(true)]
        public bool HasPrompt => Prompt.Length > 0;
        [PreviewWith("Save")]
        public string ConfirmText { get; }
        public ICommand ConfirmCommand { get; }

        [PreviewWith("Space Music Vol 1")]
        public string Text
        {
            get { return _text; }
            set { RaiseAndSetIfChanged(ref _text, value ?? string.Empty, nameof(Text)); }
        }
    }
}
