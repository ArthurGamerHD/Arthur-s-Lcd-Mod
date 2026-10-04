using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>One button of a message box: its text, what it does, and whether it is the primary choice.</summary>
    internal sealed class DialogButton
    {
        public DialogButton(string text, Action clicked = null, bool isPrimary = false)
        {
            Text = text ?? string.Empty;
            Clicked = clicked;
            IsPrimary = isPrimary;
        }

        public string Text { get; }
        public Action Clicked { get; }
        public bool IsPrimary { get; }
    }

    internal sealed class DialogButtonModel : ObservableObject
    {
        float _fontSize;

        public DialogButtonModel(DialogButton button, ICommand command, float fontSize)
        {
            Text = button.Text;
            Classes = button.IsPrimary ? "dialog-button primary" : "dialog-button";
            Command = command;
            _fontSize = fontSize;
        }

        public string Text { get; }
        public string Classes { get; }
        public ICommand Command { get; }

        public float FontSize
        {
            get { return _fontSize; }
            internal set { RaiseAndSetIfChanged(ref _fontSize, value, nameof(FontSize)); }
        }
    }

    /// <summary>A message with one to three buttons; any button closes the box before running its action.</summary>
    internal sealed class MessageBoxViewModel : DialogViewModel
    {
        readonly List<DialogButtonModel> _buttons = new List<DialogButtonModel>();

        public MessageBoxViewModel(string title, string message, IReadOnlyList<DialogButton> buttons, string icon = null)
        {
            if (buttons == null || buttons.Count < 1 || buttons.Count > 3)
                throw new ArgumentException("A message box has one to three buttons.", nameof(buttons));
            Title = title;
            Message = message ?? string.Empty;
            Icon = icon;
            for (int i = 0; i < buttons.Count; i++)
            {
                DialogButton button = buttons[i];
                _buttons.Add(new DialogButtonModel(button, new DelegateCommand(_ => Choose(button.Clicked)), FontSize));
            }
        }

        public MessageBoxViewModel(string title, string message, string primaryText, Action primary,
            string secondaryText = null, Action secondary = null, string icon = null)
            : this(title, message, string.IsNullOrEmpty(secondaryText)
                ? new[] { new DialogButton(primaryText, primary, true) }
                : new[] { new DialogButton(secondaryText, secondary), new DialogButton(primaryText, primary, true) }, icon)
        {
        }

        public string Message { get; }
        public string Icon { get; }
        public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
        public IReadOnlyList<DialogButtonModel> Buttons => _buttons;

        protected override void OnFontSizeChanged()
        {
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].FontSize = FontSize;
        }

        void Choose(Action choice)
        {
            Close();
            if (choice != null)
                choice();
        }
    }
}
