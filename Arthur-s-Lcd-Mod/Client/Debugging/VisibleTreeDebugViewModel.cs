using System.Collections.Generic;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Debugging
{
    internal sealed class VisibleTreeDebugViewModel : ObservableObject
    {
        readonly List<VisibleTreeLine> _lines = new List<VisibleTreeLine>();

        public IReadOnlyList<VisibleTreeLine> Lines => _lines;

        public void Refresh(IReadOnlyList<VisibleTreeSnapshot.Line> source)
        {
            while (_lines.Count < source.Count)
                _lines.Add(new VisibleTreeLine());
            while (_lines.Count > source.Count)
                _lines.RemoveAt(_lines.Count - 1);
            for (int i = 0; i < source.Count; i++)
                _lines[i].Update(source[i].Text, source[i].Color);
            RaisePropertyChanged<IReadOnlyList<VisibleTreeLine>>(nameof(Lines));
        }
    }

    internal sealed class VisibleTreeLine : ObservableObject
    {
        string _text;
        Color _color;

        public string Text => _text;
        public Color Color => _color;

        public void Update(string text, Color color)
        {
            if (_text != text)
            {
                _text = text;
                RaisePropertyChanged<string>(nameof(Text));
            }
            if (_color != color)
            {
                _color = color;
                RaisePropertyChanged<Color>(nameof(Color));
            }
        }
    }
}
