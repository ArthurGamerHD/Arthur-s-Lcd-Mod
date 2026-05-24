using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Helpers
{
    internal static class TextWrappingHelper
    {
        const float FallbackCharacterWidth = 16f;
        const float FallbackLineHeight = 30f;

        public static List<string> WrapText(
            string text,
            IMyTextSurface surface,
            string fontId,
            float scale,
            float maxWidth,
            float maxHeight,
            float lineSpacing,
            bool ellipsizeWhenTruncated)
        {
            var lines = new List<string>();
            if (surface == null || string.IsNullOrEmpty(text) || maxWidth <= 1f || maxHeight <= 1f)
                return lines;

            float lineHeight = GetLineHeight(surface, fontId, scale, lineSpacing);
            int maxLines = (int)Math.Floor(maxHeight / lineHeight);
            if (maxLines < 1)
                return lines;

            var builder = new PlainTextLineBuilder(lines, surface, fontId, scale, maxWidth, maxLines, ellipsizeWhenTruncated);
            AppendWrappedWords(
                text,
                () => builder.HasText,
                builder.AppendToken,
                builder.FlushLine);
            builder.Finish();
            return lines;
        }

        public static void AppendWrappedWords(
            string text,
            Func<bool> hasLineContent,
            Action<string> appendToken,
            Action flushLine)
        {
            if (appendToken == null)
                return;

            text = NormalizeNewlines(text);
            int index = 0;

            while (index < text.Length)
            {
                while (index < text.Length && IsInlineWhitespace(text[index]))
                    index++;

                if (index >= text.Length)
                    break;

                if (text[index] == '\n')
                {
                    index++;
                    if (flushLine != null)
                        flushLine();
                    continue;
                }

                int start = index;
                while (index < text.Length && !IsInlineWhitespace(text[index]) && text[index] != '\n')
                    index++;

                string word = text.Substring(start, index - start);
                bool hasContent = hasLineContent != null && hasLineContent();
                appendToken(hasContent ? " " + word : word);
            }
        }

        public static void AppendPreformattedCharacters(
            string text,
            Action<string> appendToken,
            Action flushLine)
        {
            if (appendToken == null)
                return;

            text = NormalizeNewlines(text);
            if (text.Length == 0)
            {
                if (flushLine != null)
                    flushLine();
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    if (flushLine != null)
                        flushLine();
                    continue;
                }

                appendToken(text[i].ToString());
            }
        }

        public static string NormalizeNewlines(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static bool IsInlineWhitespace(char c)
        {
            return c == ' ' || c == '\t';
        }

        public static float GetLineHeight(IMyTextSurface surface, string fontId, float scale, float lineSpacing)
        {
            if (surface == null)
                return 1f;

            float measured = MeasureText(surface, "Ag", fontId, scale).Y;
            float fallback = FallbackLineHeight * Math.Max(0.01f, scale);
            return Math.Max(1f, Math.Max(measured, fallback) + lineSpacing);
        }

        public static Vector2 MeasureText(IMyTextSurface surface, string text, string fontId, float scale)
        {
            if (surface == null || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            var measured = FormatingHelper.GetSizeInPixel(text, fontId, scale, surface);
            if (measured.X > 0f && measured.Y > 0f)
                return measured;

            float safeScale = Math.Max(0.01f, scale);
            float width = measured.X > 0f ? measured.X : EstimateWidth(text, safeScale);
            float height = measured.Y > 0f ? measured.Y : FallbackLineHeight * safeScale;
            return new Vector2(width, height);
        }

        static float EstimateWidth(string text, float scale)
        {
            int maxLineLength = 0;
            int currentLineLength = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    maxLineLength = Math.Max(maxLineLength, currentLineLength);
                    currentLineLength = 0;
                    continue;
                }

                currentLineLength++;
            }

            maxLineLength = Math.Max(maxLineLength, currentLineLength);
            return maxLineLength * FallbackCharacterWidth * scale;
        }

        public static string TrimToWidth(
            string input,
            IMyTextSurface surface,
            string fontId,
            float scale,
            float maxWidth)
        {
            if (string.IsNullOrEmpty(input) || surface == null || maxWidth <= 0f)
                return string.Empty;

            if (MeasureText(surface, input, fontId, scale).X <= maxWidth)
                return input;

            var sb = new StringBuilder(input);
            while (sb.Length > 0)
            {
                sb.Length--;
                string candidate = sb.ToString();
                if (MeasureText(surface, candidate, fontId, scale).X <= maxWidth)
                    return candidate;
            }

            return string.Empty;
        }

        public static string EnsureEllipsis(
            string input,
            IMyTextSurface surface,
            string fontId,
            float scale,
            float maxWidth)
        {
            string suffix = FormatingHelper.ELLIPSIS.ToString();
            if (surface == null || maxWidth <= 0f)
                return string.Empty;

            if (MeasureText(surface, suffix, fontId, scale).X > maxWidth)
                return TrimToWidth(suffix, surface, fontId, scale, maxWidth);

            string trimmed = (input ?? string.Empty).TrimEnd();
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
                return TrimToWidth(trimmed, surface, fontId, scale, maxWidth);

            while (trimmed.Length > 0 &&
                   MeasureText(surface, trimmed + suffix, fontId, scale).X > maxWidth)
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            }

            return trimmed + suffix;
        }

        sealed class PlainTextLineBuilder
        {
            readonly List<string> _lines;
            readonly IMyTextSurface _surface;
            readonly string _fontId;
            readonly float _scale;
            readonly float _maxWidth;
            readonly int _maxLines;
            readonly bool _ellipsizeWhenTruncated;
            readonly StringBuilder _line = new StringBuilder();

            float _lineWidth;
            bool _truncated;
            bool _stopped;

            public PlainTextLineBuilder(
                List<string> lines,
                IMyTextSurface surface,
                string fontId,
                float scale,
                float maxWidth,
                int maxLines,
                bool ellipsizeWhenTruncated)
            {
                _lines = lines;
                _surface = surface;
                _fontId = fontId;
                _scale = scale;
                _maxWidth = maxWidth;
                _maxLines = maxLines;
                _ellipsizeWhenTruncated = ellipsizeWhenTruncated;
            }

            public bool HasText
            {
                get { return _line.Length > 0; }
            }

            public void AppendToken(string token)
            {
                if (_stopped || string.IsNullOrEmpty(token))
                    return;

                float tokenWidth = MeasureText(_surface, token, _fontId, _scale).X;

                if (HasText && _lineWidth + tokenWidth > _maxWidth)
                {
                    FlushLine();
                    if (_stopped)
                        return;

                    token = token.TrimStart();
                    tokenWidth = MeasureText(_surface, token, _fontId, _scale).X;
                }

                if (tokenWidth > _maxWidth && token.Length > 1)
                {
                    AppendOversizedToken(token);
                    return;
                }

                AddText(token, tokenWidth);
            }

            public void FlushLine()
            {
                if (_stopped)
                    return;

                if (!HasText)
                {
                    AddLine(string.Empty);
                    return;
                }

                AddLine(_line.ToString());
                _line.Clear();
                _lineWidth = 0f;
            }

            public void Finish()
            {
                if (HasText && !_stopped)
                    FlushLine();

                if (_truncated && _ellipsizeWhenTruncated && _lines.Count > 0)
                    _lines[_lines.Count - 1] = EnsureEllipsis(_lines[_lines.Count - 1], _surface, _fontId, _scale, _maxWidth);
            }

            void AppendOversizedToken(string token)
            {
                for (int i = 0; i < token.Length && !_stopped; i++)
                {
                    string value = token[i].ToString();
                    float width = MeasureText(_surface, value, _fontId, _scale).X;

                    if (HasText && _lineWidth + width > _maxWidth)
                        FlushLine();

                    if (_stopped)
                        return;

                    AddText(value, width);
                }
            }

            void AddText(string value, float width)
            {
                if (_stopped || string.IsNullOrEmpty(value))
                    return;

                _line.Append(value);
                _lineWidth += width;
            }

            void AddLine(string line)
            {
                if (_lines.Count >= _maxLines)
                {
                    _truncated = true;
                    _stopped = true;
                    return;
                }

                _lines.Add(line ?? string.Empty);
            }
        }
    }
}
