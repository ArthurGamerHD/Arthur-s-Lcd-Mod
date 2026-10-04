using System;
using Adk.Gui.Core.Input;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>Asks for one line of text, typed through the LCD text box.</summary>
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

        public string Prompt { get; }
        public bool HasPrompt => Prompt.Length > 0;
        public string ConfirmText { get; }
        public ICommand ConfirmCommand { get; }

        public string Text
        {
            get { return _text; }
            set { RaiseAndSetIfChanged(ref _text, value ?? string.Empty, nameof(Text)); }
        }
    }
}
