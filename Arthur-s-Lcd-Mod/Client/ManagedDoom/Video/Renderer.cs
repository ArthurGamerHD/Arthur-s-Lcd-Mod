//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//



using System;

namespace ManagedDoom.Video
{
    public sealed class Renderer
    {
        private const float BitScale = 255.0F / 7.0F;

        // These must match ManagedDoomApp's byte alpha values. Space
        // Engineers loads GUI/font textures as sRGB and blends their decoded
        // linear values. A direct base-8 split is exact only in gamma-encoded
        // space, so use a tiny lookup optimized for the real blend path.
        private const double MiddleLayerAlpha = 227.0 / 255.0;
        private const double HighLayerAlpha = 224.0 / 255.0;

        private static readonly byte[] lowDigitBySrgb = new byte[256];
        private static readonly byte[] middleDigitBySrgb = new byte[256];
        private static readonly byte[] highDigitBySrgb = new byte[256];

        private static double[] gammaCorrectionParameters = new double[]
        {
            1.00,
            0.95,
            0.90,
            0.85,
            0.80,
            0.75,
            0.70,
            0.65,
            0.60,
            0.55,
            0.50
        };

        private Config config;

        private Palette palette;

        private DrawScreen screen;

        private MenuRenderer menu;
        private ThreeDRenderer threeD;
        private StatusBarRenderer statusBar;
        private IntermissionRenderer intermission;
        private OpeningSequenceRenderer openingSequence;
        private AutoMapRenderer autoMap;
        private FinaleRenderer finale;

        private Patch pause;

        private int wipeBandWidth;
        private int wipeBandCount;
        private int wipeHeight;
        private byte[] wipeBuffer;

        private char[] lowColorLookup = new char[256];
        private char[] middleColorLookup = new char[256];
        private char[] highColorLookup = new char[256];

        static Renderer()
        {
            BuildLayerDigitLookup();
        }

        public Renderer(Config config, GameContent content)
        {
            this.config = config;

            palette = content.Palette;

            if (config.video_highresolution)
            {
                screen = new DrawScreen(content.Wad, 640, 400);
            }
            else
            {
                screen = new DrawScreen(content.Wad, 320, 200);
            }

            config.video_gamescreensize = DoomMath.Clamp(config.video_gamescreensize, 0, MaxWindowSize);
            config.video_gammacorrection = DoomMath.Clamp(config.video_gammacorrection, 0, MaxGammaCorrectionLevel);

            menu = new MenuRenderer(content.Wad, screen);
            threeD = new ThreeDRenderer(content, screen, config.video_gamescreensize);
            statusBar = new StatusBarRenderer(content.Wad, screen);
            intermission = new IntermissionRenderer(content.Wad, screen);
            openingSequence = new OpeningSequenceRenderer(content.Wad, screen, this);
            autoMap = new AutoMapRenderer(content.Wad, screen);
            finale = new FinaleRenderer(content, screen);

            pause = Patch.FromWad(content.Wad, "M_PAUSE");

            var scale = screen.Width / 320;
            wipeBandWidth = 2 * scale;
            wipeBandCount = screen.Width / wipeBandWidth + 1;
            wipeHeight = screen.Height / scale;
            wipeBuffer = new byte[screen.Data.Length];

            palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);
        }

        public void RenderDoom(Doom doom, Fixed frameFrac)
        {
            if (doom.State == DoomState.Opening)
            {
                openingSequence.Render(doom.Opening, frameFrac);
            }
            else if (doom.State == DoomState.DemoPlayback)
            {
                RenderGame(doom.DemoPlayback.Game, frameFrac);
            }
            else if (doom.State == DoomState.Game)
            {
                RenderGame(doom.Game, frameFrac);
            }

            if (!doom.Menu.Active)
            {
                if (doom.State == DoomState.Game &&
                    doom.Game.State == GameState.Level &&
                    doom.Game.Paused)
                {
                    var scale = screen.Width / 320;
                    screen.DrawPatch(
                        pause,
                        (screen.Width - scale * pause.Width) / 2,
                        4 * scale,
                        scale);
                }
            }
        }

        public void RenderMenu(Doom doom)
        {
            if (doom.Menu.Active)
            {
                menu.Render(doom.Menu);
            }
        }

        public void RenderGame(DoomGame game, Fixed frameFrac)
        {
            if (game.Paused)
            {
                frameFrac = Fixed.One;
            }

            if (game.State == GameState.Level)
            {
                var consolePlayer = game.World.ConsolePlayer;
                var displayPlayer = game.World.DisplayPlayer;

                if (game.World.AutoMap.Visible)
                {
                    autoMap.Render(consolePlayer);
                    statusBar.Render(consolePlayer, true);
                }
                else
                {
                    threeD.Render(displayPlayer, frameFrac);
                    if (threeD.WindowSize < 8)
                    {
                        statusBar.Render(consolePlayer, true);
                    }
                    else if (threeD.WindowSize == ThreeDRenderer.MaxScreenSize)
                    {
                        statusBar.Render(consolePlayer, false);
                    }
                }

                if (config.video_displaymessage || ReferenceEquals(consolePlayer.Message, (string)DoomInfo.Strings.MSGOFF))
                {
                    if (consolePlayer.MessageTime > 0)
                    {
                        var scale = screen.Width / 320;
                        screen.DrawText(consolePlayer.Message, 0, 7 * scale, scale);
                    }
                }
            }
            else if (game.State == GameState.Intermission)
            {
                intermission.Render(game.Intermission);
            }
            else if (game.State == GameState.Finale)
            {
                finale.Render(game.Finale);
            }
        }

        public void Render(Doom doom, byte[] destination, Fixed frameFrac)
        {
            if (doom.Wiping)
            {
                RenderWipe(doom, destination);
                return;
            }

            RenderDoom(doom, frameFrac);
            RenderMenu(doom);

            var colors = palette[0];
            if (doom.State == DoomState.Game &&
                doom.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.Game.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.Opening &&
                doom.Opening.State == OpeningSequenceState.Demo &&
                doom.Opening.DemoGame.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.Opening.DemoGame.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.DemoPlayback &&
                doom.DemoPlayback.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.DemoPlayback.Game.World.ConsolePlayer)];
            }

            WriteData(colors, destination);
        }

        private void RenderWipe(Doom doom, byte[] destination)
        {
            RenderDoom(doom, Fixed.One);

            var wipe = doom.WipeEffect;
            var scale = screen.Width / 320;
            for (var i = 0; i < wipeBandCount - 1; i++)
            {
                var x1 = wipeBandWidth * i;
                var x2 = x1 + wipeBandWidth;
                var y1 = Math.Max(scale * wipe.Y[i], 0);
                var y2 = Math.Max(scale * wipe.Y[i + 1], 0);
                var dy = (float)(y2 - y1) / wipeBandWidth;
                for (var x = x1; x < x2; x++)
                {
                    var y = (int)Math.Round(y1 + dy * ((x - x1) / 2 * 2));
                    var copyLength = screen.Height - y;
                    if (copyLength > 0)
                    {
                        var srcPos = screen.Height * x;
                        var dstPos = screen.Height * x + y;
                        Array.Copy(wipeBuffer, srcPos, screen.Data, dstPos, copyLength);
                    }
                }
            }

            RenderMenu(doom);

            WriteData(palette[0], destination);
        }

        public void InitializeWipe()
        {
            Array.Copy(screen.Data, wipeBuffer, screen.Data.Length);
        }

        private void WriteData(uint[] colors, byte[] destination)
        {
            var screenData = screen.Data;
            for (var i = 0; i < screenData.Length; i++)
            {
                var color = colors[screenData[i]];
                var offset = 4 * i;
                destination[offset] = (byte)(color & 0xFF);
                destination[offset + 1] = (byte)((color >> 8) & 0xFF);
                destination[offset + 2] = (byte)((color >> 16) & 0xFF);
                destination[offset + 3] = (byte)((color >> 24) & 0xFF);
            }
        }

        public void Render(Doom doom, char[] destination, Fixed frameFrac)
        {
            if (doom.Wiping)
            {
                RenderWipe(doom, destination);
                return;
            }

            RenderDoom(doom, frameFrac);
            RenderMenu(doom);
            WriteCharData(GetCurrentPalette(doom), destination);
        }

        public void Render(
            Doom doom,
            char[] lowDestination,
            char[] middleDestination,
            char[] highDestination,
            Fixed frameFrac)
        {
            if (doom.Wiping)
            {
                RenderWipe(doom, lowDestination, middleDestination, highDestination);
                return;
            }

            RenderDoom(doom, frameFrac);
            RenderMenu(doom);
            WriteLayeredCharData(
                GetCurrentPalette(doom),
                lowDestination,
                middleDestination,
                highDestination);
        }

        private void RenderWipe(Doom doom, char[] destination)
        {
            RenderDoom(doom, Fixed.One);

            var wipe = doom.WipeEffect;
            var scale = screen.Width / 320;
            for (var i = 0; i < wipeBandCount - 1; i++)
            {
                var x1 = wipeBandWidth * i;
                var x2 = x1 + wipeBandWidth;
                var y1 = Math.Max(scale * wipe.Y[i], 0);
                var y2 = Math.Max(scale * wipe.Y[i + 1], 0);
                var dy = (float)(y2 - y1) / wipeBandWidth;
                for (var x = x1; x < x2; x++)
                {
                    var y = (int)Math.Round(y1 + dy * ((x - x1) / 2 * 2));
                    var copyLength = screen.Height - y;
                    if (copyLength > 0)
                    {
                        var srcPos = screen.Height * x;
                        var dstPos = screen.Height * x + y;
                        Array.Copy(wipeBuffer, srcPos, screen.Data, dstPos, copyLength);
                    }
                }
            }

            RenderMenu(doom);
            WriteCharData(palette[0], destination);
        }

        private void RenderWipe(
            Doom doom,
            char[] lowDestination,
            char[] middleDestination,
            char[] highDestination)
        {
            RenderDoom(doom, Fixed.One);

            var wipe = doom.WipeEffect;
            var scale = screen.Width / 320;
            for (var i = 0; i < wipeBandCount - 1; i++)
            {
                var x1 = wipeBandWidth * i;
                var x2 = x1 + wipeBandWidth;
                var y1 = Math.Max(scale * wipe.Y[i], 0);
                var y2 = Math.Max(scale * wipe.Y[i + 1], 0);
                var dy = (float)(y2 - y1) / wipeBandWidth;
                for (var x = x1; x < x2; x++)
                {
                    var y = (int)Math.Round(y1 + dy * ((x - x1) / 2 * 2));
                    var copyLength = screen.Height - y;
                    if (copyLength > 0)
                    {
                        var srcPos = screen.Height * x;
                        var dstPos = screen.Height * x + y;
                        Array.Copy(wipeBuffer, srcPos, screen.Data, dstPos, copyLength);
                    }
                }
            }

            RenderMenu(doom);
            WriteLayeredCharData(
                palette[0],
                lowDestination,
                middleDestination,
                highDestination);
        }

        private uint[] GetCurrentPalette(Doom doom)
        {
            var colors = palette[0];
            if (doom.State == DoomState.Game &&
                doom.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.Game.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.Opening &&
                doom.Opening.State == OpeningSequenceState.Demo &&
                doom.Opening.DemoGame.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.Opening.DemoGame.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.DemoPlayback &&
                doom.DemoPlayback.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.DemoPlayback.Game.World.ConsolePlayer)];
            }

            return colors;
        }

        private void WriteCharData(uint[] colors, char[] destination)
        {
            var screenData = screen.Data;
            for (var i = 0; i < screenData.Length; i++)
            {
                var color = colors[screenData[i]];
                destination[i] = ColorToChar(
                    (byte)(color & 0xFF),
                    (byte)((color >> 8) & 0xFF),
                    (byte)((color >> 16) & 0xFF));
            }
        }

        private void WriteLayeredCharData(
            uint[] colors,
            char[] lowDestination,
            char[] middleDestination,
            char[] highDestination)
        {
            // Convert each palette entry once, then use the 8-bit screen data
            // as a lookup index. This avoids doing RGB888/base-8 conversion
            // for all 64,000 pixels on every rendered frame.
            for (var i = 0; i < colors.Length; i++)
            {
                var color = colors[i];
                ColorToLayerChars(
                    (byte)(color & 0xFF),
                    (byte)((color >> 8) & 0xFF),
                    (byte)((color >> 16) & 0xFF),
                    out lowColorLookup[i],
                    out middleColorLookup[i],
                    out highColorLookup[i]);
            }

            var screenData = screen.Data;
            for (var i = 0; i < screenData.Length; i++)
            {
                var paletteIndex = screenData[i];
                lowDestination[i] = lowColorLookup[paletteIndex];
                middleDestination[i] = middleColorLookup[paletteIndex];
                highDestination[i] = highColorLookup[paletteIndex];
            }
        }

        public static char ColorToChar(byte r, byte g, byte b)
        {
            var c = ((int)Math.Round(r / BitScale) << 6) + ((int)Math.Round(g / BitScale) << 3) +
                ((int)Math.Round(b / BitScale));
            return (char)(0xe100 + c);
        }

        public static void ColorToLayerChars(
            byte r,
            byte g,
            byte b,
            out char low,
            out char middle,
            out char high)
        {
            low = ColorDigitsToChar(
                lowDigitBySrgb[r],
                lowDigitBySrgb[g],
                lowDigitBySrgb[b]);

            middle = ColorDigitsToChar(
                middleDigitBySrgb[r],
                middleDigitBySrgb[g],
                middleDigitBySrgb[b]);

            high = ColorDigitsToChar(
                highDigitBySrgb[r],
                highDigitBySrgb[g],
                highDigitBySrgb[b]);
        }

        private static void BuildLayerDigitLookup()
        {
            var lowWeight = (1.0 - MiddleLayerAlpha) * (1.0 - HighLayerAlpha);
            var middleWeight = MiddleLayerAlpha * (1.0 - HighLayerAlpha);
            var highWeight = HighLayerAlpha;

            var digitLinear = new double[8];
            for (var digit = 0; digit < digitLinear.Length; digit++)
            {
                digitLinear[digit] = SrgbToLinear(digit / 7.0);
            }

            for (var value = 0; value < 256; value++)
            {
                var target = SrgbToLinear(value / 255.0);
                var bestError = double.MaxValue;
                byte bestLow = 0;
                byte bestMiddle = 0;
                byte bestHigh = 0;

                for (byte high = 0; high < 8; high++)
                {
                    for (byte middle = 0; middle < 8; middle++)
                    {
                        for (byte low = 0; low < 8; low++)
                        {
                            var blended =
                                digitLinear[low] * lowWeight +
                                digitLinear[middle] * middleWeight +
                                digitLinear[high] * highWeight;
                            var error = Math.Abs(blended - target);

                            if (error < bestError)
                            {
                                bestError = error;
                                bestLow = low;
                                bestMiddle = middle;
                                bestHigh = high;
                            }
                        }
                    }
                }

                lowDigitBySrgb[value] = bestLow;
                middleDigitBySrgb[value] = bestMiddle;
                highDigitBySrgb[value] = bestHigh;
            }
        }

        private static double SrgbToLinear(double value)
        {
            if (value <= 0.04045)
            {
                return value / 12.92;
            }

            return Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private static char ColorDigitsToChar(int r, int g, int b)
        {
            return (char)(0xe100 + (r << 6) + (g << 3) + b);
        }

        private static int GetPaletteNumber(Player player)
        {
            var count = player.DamageCount;

            if (player.Powers[(int)PowerType.Strength] != 0)
            {
                // Slowly fade the berzerk out.
                var bzc = 12 - (player.Powers[(int)PowerType.Strength] >> 6);
                if (bzc > count)
                {
                    count = bzc;
                }
            }

            int palette;

            if (count != 0)
            {
                palette = (count + 7) >> 3;

                if (palette >= Palette.DamageCount)
                {
                    palette = Palette.DamageCount - 1;
                }

                palette += Palette.DamageStart;
            }
            else if (player.BonusCount != 0)
            {
                palette = (player.BonusCount + 7) >> 3;

                if (palette >= Palette.BonusCount)
                {
                    palette = Palette.BonusCount - 1;
                }

                palette += Palette.BonusStart;
            }
            else if (player.Powers[(int)PowerType.IronFeet] > 4 * 32 ||
                (player.Powers[(int)PowerType.IronFeet] & 8) != 0)
            {
                palette = Palette.IronFeet;
            }
            else
            {
                palette = 0;
            }

            return palette;
        }

        public int Width => screen.Width;
        public int Height => screen.Height;

        public int WipeBandCount => wipeBandCount;
        public int WipeHeight => wipeHeight;

        public int MaxWindowSize
        {
            get
            {
                return ThreeDRenderer.MaxScreenSize;
            }
        }

        public int WindowSize
        {
            get
            {
                return threeD.WindowSize;
            }

            set
            {
                config.video_gamescreensize = value;
                threeD.WindowSize = value;
            }
        }

        public bool DisplayMessage
        {
            get
            {
                return config.video_displaymessage;
            }

            set
            {
                config.video_displaymessage = value;
            }
        }

        public int MaxGammaCorrectionLevel
        {
            get
            {
                return gammaCorrectionParameters.Length - 1;
            }
        }

        public int GammaCorrectionLevel
        {
            get
            {
                return config.video_gammacorrection;
            }

            set
            {
                config.video_gammacorrection = value;
                palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);
            }
        }
    }
}
