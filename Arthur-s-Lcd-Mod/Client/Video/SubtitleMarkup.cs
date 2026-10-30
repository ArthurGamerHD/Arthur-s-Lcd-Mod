using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRageMath;

namespace LcdMod.Client.Video
{
    internal enum SubtitleSizeUnit
    {
        None,
        /// <summary>HTML's 1-7 font sizes, as a factor of the normal size 3.</summary>
        Relative,
        /// <summary>Pixels of whatever picture the track was authored for, which need not be the one it ships with.</summary>
        Pixels
    }

    internal struct SubtitleSpan
    {
        public string Text;
        public bool IsLineBreak;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;
        public Color? Color;
        public string Face;
        public SubtitleSizeUnit SizeUnit;
        public float Size;
    }

    /// <summary>
    /// Turns absolute subtitle pixel sizes into factors of the normal caption size. Tracks are often authored for a
    /// different resolution than the video they ship with, so the first pixel size seen counts as normal and later ones
    /// scale against it.
    /// </summary>
    internal sealed class SubtitleSizeReference
    {
        const float MinFactor = .5f;
        const float MaxFactor = 2f;

        float _reference;

        public void Reset()
        {
            _reference = 0f;
        }

        public float Factor(float pixels)
        {
            if (pixels <= 0f)
                return 1f;
            if (_reference <= 0f)
                _reference = pixels;
            return MathHelper.Clamp(pixels / _reference, MinFactor, MaxFactor);
        }
    }

    /// <summary>
    /// Best-effort reader for the HTML-like markup subtitle tracks carry (&lt;i&gt;, &lt;b&gt;, &lt;u&gt;, &lt;s&gt;,
    /// &lt;font face size color&gt;, &lt;br&gt;, entities). Unknown tags and ASS override blocks are dropped rather than shown.
    /// </summary>
    internal static class SubtitleMarkup
    {
        // HTML font sizes 1-7 (x-small to xxx-large) relative to the default size 3.
        static readonly float[] HtmlSizes = { 10f / 16f, 13f / 16f, 1f, 18f / 16f, 24f / 16f, 2f, 3f };

        static readonly Dictionary<string, Color> NamedColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "white", new Color(255, 255, 255) }, { "black", new Color(0, 0, 0) }, { "red", new Color(255, 0, 0) },
            { "lime", new Color(0, 255, 0) }, { "green", new Color(0, 128, 0) }, { "blue", new Color(0, 0, 255) },
            { "yellow", new Color(255, 255, 0) }, { "cyan", new Color(0, 255, 255) }, { "aqua", new Color(0, 255, 255) },
            { "magenta", new Color(255, 0, 255) }, { "fuchsia", new Color(255, 0, 255) }, { "gray", new Color(128, 128, 128) },
            { "grey", new Color(128, 128, 128) }, { "silver", new Color(192, 192, 192) }, { "maroon", new Color(128, 0, 0) },
            { "olive", new Color(128, 128, 0) }, { "navy", new Color(0, 0, 128) }, { "teal", new Color(0, 128, 128) },
            { "purple", new Color(128, 0, 128) }, { "orange", new Color(255, 165, 0) }, { "pink", new Color(255, 192, 203) },
            { "brown", new Color(165, 42, 42) }, { "gold", new Color(255, 215, 0) }
        };

        struct Tag
        {
            public string Name;
            public SubtitleSpan Style;
        }

        public static List<SubtitleSpan> Parse(string markup)
        {
            var spans = new List<SubtitleSpan>();
            if (string.IsNullOrEmpty(markup))
                return spans;

            var stack = new List<Tag>();
            var style = new SubtitleSpan();
            var text = new StringBuilder();
            int i = 0;
            while (i < markup.Length)
            {
                char c = markup[i];
                if (c == '\n' || (c == '\\' && i + 1 < markup.Length && (markup[i + 1] == 'N' || markup[i + 1] == 'n')))
                {
                    Flush(spans, text, style);
                    spans.Add(new SubtitleSpan { IsLineBreak = true });
                    i += c == '\n' ? 1 : 2;
                    continue;
                }

                if (c == '{' && i + 1 < markup.Length && markup[i + 1] == '\\')
                {
                    int close = markup.IndexOf('}', i);
                    if (close > i)
                    {
                        i = close + 1;
                        continue;
                    }
                }

                if (c == '<')
                {
                    int close = markup.IndexOf('>', i + 1);
                    if (close > i + 1 && IsTagStart(markup[i + 1]))
                    {
                        Flush(spans, text, style);
                        ApplyTag(markup.Substring(i + 1, close - i - 1), stack, ref style, spans);
                        i = close + 1;
                        continue;
                    }
                }

                if (c == '&')
                {
                    int end = markup.IndexOf(';', i + 1);
                    string decoded;
                    if (end > i + 1 && end - i <= 10 && TryDecodeEntity(markup.Substring(i + 1, end - i - 1), out decoded))
                    {
                        text.Append(decoded);
                        i = end + 1;
                        continue;
                    }
                }

                if (c != '\r')
                    text.Append(c);
                i++;
            }

            Flush(spans, text, style);
            return spans;
        }

        static bool IsTagStart(char c)
        {
            return char.IsLetter(c) || c == '/';
        }

        static void Flush(List<SubtitleSpan> spans, StringBuilder text, SubtitleSpan style)
        {
            if (text.Length == 0)
                return;
            style.Text = text.ToString();
            style.IsLineBreak = false;
            spans.Add(style);
            text.Clear();
        }

        static void ApplyTag(string tag, List<Tag> stack, ref SubtitleSpan style, List<SubtitleSpan> spans)
        {
            tag = tag.Trim();
            bool closing = tag.StartsWith("/", StringComparison.Ordinal);
            if (closing)
                tag = tag.Substring(1).Trim();
            bool selfClosing = tag.EndsWith("/", StringComparison.Ordinal);
            if (selfClosing)
                tag = tag.Substring(0, tag.Length - 1).Trim();

            int nameEnd = 0;
            while (nameEnd < tag.Length && !char.IsWhiteSpace(tag[nameEnd]))
                nameEnd++;
            string name = tag.Substring(0, nameEnd).ToLowerInvariant();

            if (name == "br")
            {
                spans.Add(new SubtitleSpan { IsLineBreak = true });
                return;
            }

            if (closing)
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].Name != name)
                        continue;
                    style = stack[i].Style;
                    stack.RemoveRange(i, stack.Count - i);
                    return;
                }
                return;
            }

            var previous = style;
            switch (name)
            {
                case "b":
                case "strong":
                    style.Bold = true;
                    break;
                case "i":
                case "em":
                    style.Italic = true;
                    break;
                case "u":
                    style.Underline = true;
                    break;
                case "s":
                case "strike":
                case "del":
                    style.Strikethrough = true;
                    break;
                case "font":
                    ApplyFontAttributes(tag.Substring(nameEnd), ref style);
                    break;
                default:
                    return;
            }

            if (!selfClosing)
                stack.Add(new Tag { Name = name, Style = previous });
            else
                style = previous;
        }

        static void ApplyFontAttributes(string attributes, ref SubtitleSpan style)
        {
            int i = 0;
            while (i < attributes.Length)
            {
                while (i < attributes.Length && char.IsWhiteSpace(attributes[i]))
                    i++;
                int nameStart = i;
                while (i < attributes.Length && attributes[i] != '=' && !char.IsWhiteSpace(attributes[i]))
                    i++;
                string name = attributes.Substring(nameStart, i - nameStart);
                while (i < attributes.Length && char.IsWhiteSpace(attributes[i]))
                    i++;
                if (i >= attributes.Length || attributes[i] != '=')
                {
                    if (name.Length == 0)
                        i++;
                    continue;
                }
                i++;
                while (i < attributes.Length && char.IsWhiteSpace(attributes[i]))
                    i++;

                string value;
                if (i < attributes.Length && (attributes[i] == '"' || attributes[i] == '\''))
                {
                    char quote = attributes[i];
                    int end = attributes.IndexOf(quote, i + 1);
                    if (end < 0)
                        end = attributes.Length;
                    value = attributes.Substring(i + 1, end - i - 1);
                    i = end + 1;
                }
                else
                {
                    int start = i;
                    while (i < attributes.Length && !char.IsWhiteSpace(attributes[i]))
                        i++;
                    value = attributes.Substring(start, i - start);
                }

                ApplyFontAttribute(name.ToLowerInvariant(), value.Trim(), ref style);
            }
        }

        static void ApplyFontAttribute(string name, string value, ref SubtitleSpan style)
        {
            switch (name)
            {
                case "color":
                    Color color;
                    if (TryParseColor(value, out color))
                        style.Color = color;
                    break;
                case "face":
                    int comma = value.IndexOf(',');
                    string face = (comma >= 0 ? value.Substring(0, comma) : value).Trim().Trim('"', '\'');
                    if (face.Length > 0)
                        style.Face = face;
                    break;
                case "size":
                    ApplySize(value, ref style);
                    break;
            }
        }

        static void ApplySize(string value, ref SubtitleSpan style)
        {
            bool pixels = value.EndsWith("px", StringComparison.OrdinalIgnoreCase);
            if (pixels)
                value = value.Substring(0, value.Length - 2).Trim();
            bool relativeStep = !pixels && (value.StartsWith("+", StringComparison.Ordinal) || value.StartsWith("-", StringComparison.Ordinal));

            float size;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
                return;

            if (relativeStep || (!pixels && size >= 1f && size <= 7f && size == (float)Math.Floor(size)))
            {
                int step = (int)Math.Round(relativeStep ? 3f + size : size);
                step = Math.Max(1, Math.Min(7, step));
                style.SizeUnit = SubtitleSizeUnit.Relative;
                style.Size = HtmlSizes[step - 1];
                return;
            }

            if (size > 0f)
            {
                style.SizeUnit = SubtitleSizeUnit.Pixels;
                style.Size = size;
            }
        }

        internal static bool TryParseColor(string value, out Color color)
        {
            color = default(Color);
            if (string.IsNullOrEmpty(value))
                return false;
            if (NamedColors.TryGetValue(value, out color))
                return true;

            string hex = value;
            bool ass = false;
            if (hex.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2).TrimEnd('&');
                ass = true;
            }
            else if (hex.StartsWith("#", StringComparison.Ordinal))
                hex = hex.Substring(1);
            else if (hex.Length != 6 && hex.Length != 8)
                return false;

            uint packed;
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed))
                return false;

            if (ass)
            {
                // ASS colours are &HAABBGGRR, with alpha counting transparency.
                byte alpha = (byte)(255 - (hex.Length > 6 ? (packed >> 24) & 0xFF : 0));
                color = new Color((byte)(packed & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)((packed >> 16) & 0xFF), alpha);
                return true;
            }

            switch (hex.Length)
            {
                case 3:
                    color = new Color((byte)(((packed >> 8) & 0xF) * 17), (byte)(((packed >> 4) & 0xF) * 17), (byte)((packed & 0xF) * 17));
                    return true;
                case 6:
                    color = new Color((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
                    return true;
                case 8:
                    color = new Color((byte)((packed >> 24) & 0xFF), (byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF),
                        (byte)(packed & 0xFF));
                    return true;
            }
            return false;
        }

        static bool TryDecodeEntity(string entity, out string text)
        {
            text = null;
            switch (entity.ToLowerInvariant())
            {
                case "amp": text = "&"; return true;
                case "lt": text = "<"; return true;
                case "gt": text = ">"; return true;
                case "quot": text = "\""; return true;
                case "apos": text = "'"; return true;
                case "nbsp": text = " "; return true;
            }

            if (entity.Length < 2 || entity[0] != '#')
                return false;
            int code;
            bool parsed = entity[1] == 'x' || entity[1] == 'X'
                ? int.TryParse(entity.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)
                : int.TryParse(entity.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
            if (!parsed || code <= 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
                return false;
            text = char.ConvertFromUtf32(code);
            return true;
        }
    }
}
