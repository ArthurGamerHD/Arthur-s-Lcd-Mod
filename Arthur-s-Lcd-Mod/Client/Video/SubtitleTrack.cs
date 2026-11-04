using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LcdMod.Client.Video
{
    /// <summary>
    /// Timed subtitle cues read from an SRT or WebVTT file. The text keeps its markup for <see cref="SubtitleMarkup"/>.
    /// </summary>
    internal sealed class SubtitleTrack
    {
        public static readonly string[] Extensions = { ".srt", ".vtt" };

        struct Cue
        {
            public double Start;
            public double End;
            public string Text;
        }

        readonly List<Cue> _cues = new List<Cue>();

        public int Count => _cues.Count;

        public string TextAt(double seconds)
        {
            string text = null;
            for (int i = 0; i < _cues.Count; i++)
            {
                Cue cue = _cues[i];
                if (cue.Start > seconds)
                    break;
                if (seconds < cue.End)
                    text = text == null ? cue.Text : text + "\n" + cue.Text;
            }

            return text;
        }

        // Reads SRT or WebVTT text; blocks without a valid timing line are skipped.
        public static SubtitleTrack Parse(string content)
        {
            var track = new SubtitleTrack();
            if (string.IsNullOrEmpty(content))
                return track;

            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                double start, end;
                if (!TryParseTiming(lines[i], out start, out end))
                    continue;

                var text = new StringBuilder();
                for (i++; i < lines.Length && lines[i].Trim().Length > 0; i++)
                {
                    if (text.Length > 0)
                        text.Append('\n');
                    text.Append(lines[i].Trim());
                }

                if (text.Length > 0 && end > start)
                    track._cues.Add(new Cue { Start = start, End = end, Text = text.ToString() });
            }

            track._cues.Sort((left, right) => left.Start.CompareTo(right.Start));
            return track;
        }

        // The track as SRT, for sending it to the players a video is streamed to.
        public byte[] ToBytes()
        {
            var text = new StringBuilder();
            for (int i = 0; i < _cues.Count; i++)
            {
                Cue cue = _cues[i];
                text.Append(i + 1).Append('\n')
                    .Append(FormatTime(cue.Start)).Append(" --> ").Append(FormatTime(cue.End)).Append('\n')
                    .Append(cue.Text).Append("\n\n");
            }

            return Encoding.UTF8.GetBytes(text.ToString());
        }

        public static SubtitleTrack FromBytes(byte[] data)
        {
            return Parse(data == null ? null : Encoding.UTF8.GetString(data));
        }

        static bool TryParseTiming(string line, out double start, out double end)
        {
            start = end = 0d;
            int arrow = line.IndexOf("-->", StringComparison.Ordinal);
            if (arrow < 0)
                return false;

            string right = line.Substring(arrow + 3).Trim();
            int settings = right.IndexOf(' ');
            if (settings >= 0)
                right = right.Substring(0, settings);
            return TryParseTime(line.Substring(0, arrow).Trim(), out start) && TryParseTime(right, out end);
        }

        // SRT writes 00:01:02,345 and WebVTT 00:01:02.345 or 01:02.345.
        static bool TryParseTime(string text, out double seconds)
        {
            seconds = 0d;
            string[] parts = text.Replace(',', '.').Split(':');
            if (parts.Length < 2 || parts.Length > 3)
                return false;

            double total = 0d;
            for (int i = 0; i < parts.Length; i++)
            {
                double value;
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0d)
                    return false;
                total = total * 60d + value;
            }

            seconds = total;
            return true;
        }

        static string FormatTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
            return ((int)time.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" +
                   time.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   time.Seconds.ToString("00", CultureInfo.InvariantCulture) + "," +
                   time.Milliseconds.ToString("000", CultureInfo.InvariantCulture);
        }
    }
}
