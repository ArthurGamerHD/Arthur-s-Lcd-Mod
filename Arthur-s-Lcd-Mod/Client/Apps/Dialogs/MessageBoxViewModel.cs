using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using Adk.Mvvm;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>One button of a message box: its text, what it does, and whether it is the primary choice.</summary>
    internal sealed class DialogButton
    {
        public DialogButton(string text, Action clicked = null, bool isPrimary = false, bool isDangerous = false)
        {
            Text = text ?? string.Empty;
            Clicked = clicked;
            IsPrimary = isPrimary;
            IsDangerous = isDangerous;
        }

        public string Text { get; }
        public Action Clicked { get; }
        public bool IsPrimary { get; }
        public bool IsDangerous { get; }
    }

    internal sealed class DialogButtonModel : ObservableObject
    {
        float _fontSize;

        public DialogButtonModel(DialogButton button, ICommand command, float fontSize)
        {
            Text = button.Text;
            Classes = $"dialog-button {(button.IsDangerous ? "danger" : button.IsPrimary ? "primary" : "")}".TrimEnd();
            Command = command;
            _fontSize = fontSize;
        }

        [PreviewWith("Cancel", "Delete")]
        public string Text { get; }
        [PreviewWith("dialog-button", "dialog-button danger")]
        public string Classes { get; }
        public ICommand Command { get; }

        [PreviewWith(0.65f)]
        public float FontSize
        {
            get { return _fontSize; }
            internal set { RaiseAndSetIfChanged(ref _fontSize, value, nameof(FontSize)); }
        }
    }

    /// <summary>A message with one to three buttons; any button closes the box before running its action.</summary>
    [PreviewWith("Title", "Delete playlist")]
    [PreviewWith("SmallFontSize", 0.5525f)]
    [PreviewWith("IsRegular", true)]
    [PreviewWith("IsCompact", false)]
    [PreviewWith("FontSize", 0.65f)]
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

        [PreviewWith("Delete the playlist \"Space Music Vol 1\"? This cannot be undone.")]
        public string Message { get; }
        [PreviewWith("Warning")]
        public string Icon { get; }
        [PreviewWith(true)]
        public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
        [PreviewWith(2)]
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
