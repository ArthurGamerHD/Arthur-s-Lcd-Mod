using System;
using System.Collections.Generic;
using System.Text;
using VRage.Collections;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls
{
    // The crash screen of a screen whose grid's power is disrupted: the frame cut into horizontal bands, each a
    // different blue, sliding left and right on its own, with its text losing characters to random ones and the QR
    // code always cut in three parts that never line up.
    sealed class GlitchedBSoD
    {
        const int MINIMUM_BANDS = 12;
        const int MAXIMUM_BANDS = 28;
        // The bands are cut anew this often, in seconds, give or take half of it.
        const double RESHUFFLE_SECONDS = 1.2d;
        // Of the screen's width, the farthest a band slides.
        const float MAXIMUM_SHIFT = .09f;
        const float GLITCHED_CHARACTERS = .18f;
        const int QR_PARTS = 3;
        // Of the QR code's width, how far apart its parts stand besides their slide.
        const float QR_PART_OFFSET = .3f;
        static readonly Color CrashBackground = new Color(0, 88, 151);

        struct Band
        {
            public float Top;
            public float Height;
            public float Amplitude;
            public float Speed;
            public float Phase;
            // A fixed shift on top of the slide, in pixels (the QR code's parts).
            public float Offset;
            public Color Tone;
            public Color TextTint;
        }

        readonly List<MySprite> _frame;
        readonly RectangleF _viewBox;
        readonly Random _random;
        readonly List<Band> _bands = new List<Band>();
        readonly StringBuilder _text = new StringBuilder();
        double _reshuffleAt;
        // The QR code's rows, as fractions of the view's height; no QR code when not found.
        readonly bool _hasQr;
        readonly float _qrTop;
        readonly float _qrBottom;
        readonly float _qrWidth;

        public GlitchedBSoD(ListReader<MySprite> frame, RectangleF viewBox, int seed)
        {
            _frame = new List<MySprite>(frame.Count);
            for (int i = 0; i < frame.Count; i++)
                _frame.Add(frame[i]);
            _viewBox = viewBox;
            _random = new Random(seed);
            // The QR code is the frame's one picture: every other texture is a plain square.
            for (int i = 0; i < _frame.Count && viewBox.Height > 0f; i++)
            {
                MySprite sprite = _frame[i];
                if (sprite.Type != SpriteType.TEXTURE || sprite.Data == "SquareSimple" || !sprite.Position.HasValue ||
                    !sprite.Size.HasValue)
                    continue;
                Vector2 size = sprite.Size.Value;
                if (size.Y <= 0f || Math.Abs(size.X - size.Y) > 2f)
                    continue;
                _hasQr = true;
                _qrTop = (sprite.Position.Value.Y - size.Y * .5f - viewBox.Y) / viewBox.Height;
                _qrBottom = _qrTop + size.Y / viewBox.Height;
                _qrWidth = size.X;
                break;
            }
        }

        // The frame at this time, in seconds, into output (cleared first).
        public void Draw(double seconds, List<MySprite> output)
        {
            output.Clear();
            if (_bands.Count == 0 || seconds >= _reshuffleAt)
            {
                Cut();
                _reshuffleAt = seconds + RESHUFFLE_SECONDS * (.5d + _random.NextDouble());
            }
            for (int b = 0; b < _bands.Count; b++)
                DrawBand(_bands[b], seconds, output);
            output.Add(MySprite.CreateClearClipRect());
        }

        void Cut()
        {
            _bands.Clear();
            int count = _random.Next(MINIMUM_BANDS, MAXIMUM_BANDS + 1);
            var cuts = new List<float>();
            for (int i = 0; i < count - 1; i++)
            {
                float cut = (float)_random.NextDouble();
                if (!_hasQr || cut <= _qrTop || cut >= _qrBottom)
                    cuts.Add(cut);
            }
            if (_hasQr)
                for (int i = 0; i <= QR_PARTS; i++)
                    cuts.Add(_qrTop + (_qrBottom - _qrTop) * i / QR_PARTS);
            cuts.Add(1f);
            cuts.Sort();
            // The QR code's parts, each shifted its own way: left, centre and right, in some order.
            int part = 0;
            int first = _random.Next(QR_PARTS);
            float top = 0f;
            for (int i = 0; i < cuts.Count; i++)
            {
                float bottom = Math.Min(1f, cuts[i]);
                if (bottom - top > .002f)
                {
                    Band band = NewBand(top, bottom - top);
                    if (_hasQr && top >= _qrTop - 1e-4f && bottom <= _qrBottom + 1e-4f)
                    {
                        band.Offset = ((first + part++) % QR_PARTS - 1) * _qrWidth * QR_PART_OFFSET;
                        band.Amplitude = Math.Max(band.Amplitude, MAXIMUM_SHIFT * .3f);
                    }
                    _bands.Add(band);
                }
                top = Math.Max(top, bottom);
            }
        }

        Band NewBand(float top, float height)
        {
            // Mostly the crash screen's blue, a few deeper or paler, now and then one far off.
            float shade = (float)(_random.NextDouble() * 2d - 1d);
            bool wild = _random.NextDouble() < .15d;
            var tone = new Color(
                (int)MathHelper.Clamp(CrashBackground.R + shade * 25f + (wild ? 40 : 0), 0f, 255f),
                (int)MathHelper.Clamp(CrashBackground.G + shade * 45f, 0f, 255f),
                (int)MathHelper.Clamp(CrashBackground.B + shade * 70f + (wild ? 60 : 0), 0f, 255f));
            float still = _random.NextDouble() < .25d ? .1f : 1f;
            return new Band
            {
                Top = top,
                Height = height,
                Amplitude = (float)_random.NextDouble() * MAXIMUM_SHIFT * still,
                Speed = .6f + (float)_random.NextDouble() * 5f,
                Phase = (float)(_random.NextDouble() * Math.PI * 2d),
                Tone = tone,
                TextTint = Color.Lerp(Color.White, tone, (float)_random.NextDouble() * .35f)
            };
        }

        void DrawBand(Band band, double seconds, List<MySprite> output)
        {
            float shift = (float)(Math.Sin(seconds * band.Speed + band.Phase) * band.Amplitude) * _viewBox.Width +
                          band.Offset;
            // A jolt now and then, beyond the slide.
            if (_random.NextDouble() < .04d)
                shift += (float)(_random.NextDouble() * 2d - 1d) * _viewBox.Width * MAXIMUM_SHIFT;
            var clip = new Rectangle(
                (int)_viewBox.X,
                (int)Math.Floor(_viewBox.Y + band.Top * _viewBox.Height),
                (int)Math.Ceiling(_viewBox.Width),
                Math.Max(1, (int)Math.Ceiling(band.Height * _viewBox.Height)));
            output.Add(MySprite.CreateClipRect(clip));
            output.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(clip.X + clip.Width * .5f, clip.Y + clip.Height * .5f),
                new Vector2(clip.Width, clip.Height), band.Tone));
            for (int i = 0; i < _frame.Count; i++)
            {
                MySprite sprite = _frame[i];
                switch (sprite.Type)
                {
                    case SpriteType.CLIP_RECT:
                        // The game keeps one clip at a time: the frame's own clips are cut down to the band.
                        output.Add(sprite.Position.HasValue && sprite.Size.HasValue
                            ? MySprite.CreateClipRect(Intersect(clip, sprite, shift))
                            : MySprite.CreateClipRect(clip));
                        continue;
                    case SpriteType.TEXT:
                        sprite.Data = Scramble(sprite.Data);
                        if (sprite.Color.HasValue)
                            sprite.Color = Tint(sprite.Color.Value, band);
                        break;
                    case SpriteType.TEXTURE:
                        if (sprite.Color.HasValue)
                            sprite.Color = sprite.Color.Value == CrashBackground ? band.Tone : Tint(sprite.Color.Value, band);
                        break;
                }
                if (sprite.Position.HasValue)
                    sprite.Position = sprite.Position.Value + new Vector2(shift, 0f);
                output.Add(sprite);
            }
            output.Add(MySprite.CreateClipRect(clip));
        }

        static Rectangle Intersect(Rectangle band, MySprite clip, float shift)
        {
            Vector2 position = clip.Position.Value + new Vector2(shift, 0f);
            Vector2 size = clip.Size.Value;
            int left = Math.Max(band.X, (int)position.X);
            int top = Math.Max(band.Y, (int)position.Y);
            int right = Math.Min(band.X + band.Width, (int)(position.X + size.X));
            int bottom = Math.Min(band.Y + band.Height, (int)(position.Y + size.Y));
            return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        Color Tint(Color color, Band band)
        {
            Color tinted = Color.Lerp(color, band.TextTint, .5f);
            tinted.A = color.A;
            return tinted;
        }

        // Some of the characters swapped for random ones, anew every frame; spaces and line breaks stay.
        string Scramble(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            _text.Clear();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != ' ' && c != '\n' && _random.NextDouble() < GLITCHED_CHARACTERS)
                    c = (char)_random.Next(33, 127);
                _text.Append(c);
            }
            return _text.ToString();
        }
    }
}
