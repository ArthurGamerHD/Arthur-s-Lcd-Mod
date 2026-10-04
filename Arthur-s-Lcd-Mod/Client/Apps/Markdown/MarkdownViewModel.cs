using LcdMod.Client.Markdown;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.Markdown
{
    internal sealed class MarkdownViewModel : ObservableObject
    {
        readonly MarkdownApp _app;
        MarkdownDocument _document;

        public MarkdownViewModel(MarkdownApp app)
        {
            _app = app;
        }

        public MarkdownDocument Document => _document;

        public void Refresh()
        {
            if (ReferenceEquals(_document, _app.Document))
                return;
            _document = _app.Document;
            RaisePropertyChanged<MarkdownDocument>(nameof(Document));
        }
    }
}
