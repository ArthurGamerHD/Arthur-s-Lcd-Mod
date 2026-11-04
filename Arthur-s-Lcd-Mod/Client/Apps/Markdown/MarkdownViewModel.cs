using LcdMod.Client.Markdown;
using Adk.Mvvm;

namespace LcdMod.Client.Apps.Markdown
{
    /// <summary>What the markdown view model reads from its app (<see cref="MarkdownApp"/>; a design-time host in the preview).</summary>
    internal interface IMarkdownHost
    {
        MarkdownDocument Document { get; }
    }

    internal sealed class MarkdownViewModel : ObservableObject
    {
        readonly IMarkdownHost _app;
        MarkdownDocument _document;

        public MarkdownViewModel(IMarkdownHost app)
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
