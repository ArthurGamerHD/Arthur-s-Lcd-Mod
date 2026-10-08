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

namespace ManagedDoom
{
    public sealed class GameContent : IDisposable
    {
        private Wad wad;
        private Palette palette;
        private ColorMap colorMap;
        private ITextureLookup textures;
        private IFlatLookup flats;
        private ISpriteLookup sprites;
        private TextureAnimation animation;

        private GameContent()
        {
        }

        public GameContent(CommandLineArgs args)
        {
            throw new NotSupportedException("Desktop WAD discovery is not available inside Space Engineers. Use GameContent.FromWadBytes instead.");
        }

        public static GameContent FromBase64Wad(string base64Wad, CommandLineArgs args)
        {
            var gc = new GameContent();

            gc.wad = Wad.FromBase64(base64Wad);
            DeHackEd.Initialize(args, gc.wad);
            gc.palette = new Palette(gc.wad);
            gc.colorMap = new ColorMap(gc.wad);
            gc.textures = new TextureLookup(gc.wad);
            gc.flats = new FlatLookup(gc.wad);
            gc.sprites = new SpriteLookup(gc.wad);
            gc.animation = new TextureAnimation(gc.textures, gc.flats);

            return gc;
        }

        public static GameContent FromWadBytes(string name, byte[] wadBytes, CommandLineArgs args)
        {
            var gc = new GameContent();

            gc.wad = Wad.FromBytes(name, wadBytes);
            DeHackEd.Initialize(args, gc.wad);
            gc.palette = new Palette(gc.wad);
            gc.colorMap = new ColorMap(gc.wad);
            gc.textures = new TextureLookup(gc.wad);
            gc.flats = new FlatLookup(gc.wad);
            gc.sprites = new SpriteLookup(gc.wad);
            gc.animation = new TextureAnimation(gc.textures, gc.flats);

            return gc;
        }

        public static GameContent CreateDummy(params string[] wadPaths)
        {
            throw new NotSupportedException("Desktop WAD paths are not available inside Space Engineers. Use GameContent.FromWadBytes instead.");
        }

        public void Dispose()
        {
            if (wad != null)
            {
                wad.Dispose();
                wad = null;
            }
        }

        public Wad Wad => wad;
        public Palette Palette => palette;
        public ColorMap ColorMap => colorMap;
        public ITextureLookup Textures => textures;
        public IFlatLookup Flats => flats;
        public ISpriteLookup Sprites => sprites;
        public TextureAnimation Animation => animation;
    }
}
