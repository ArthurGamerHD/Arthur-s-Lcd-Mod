
using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph
{
    public enum CursorType
    {
        None,
        AppStarting,
        Arrow,
        Default,
        Hand,
        Cross,
        Help,
        WaitCursor,
        No
    }

    public class Cursor
    {
        public static void AddCursor(List<MySprite> sprites, CursorType cursor, Vector2 position, Vector2 size, float scale = 1)
        {
            if(cursor == CursorType.None)
                return;

            float rotation = 0;
            if (cursor == CursorType.WaitCursor || cursor == CursorType.AppStarting)
                rotation = (float)(MyAPIGateway.Session.GameplayFrameCounter/60f % 6.283185307179586d);

            if (cursor >= CursorType.Cross)
            {
                position += size * scale/4;
                scale *= 0.8f;
            }


            if (cursor == CursorType.AppStarting)
            {
                sprites.Add( new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "CursorDefault",
                    Position = position,
                    Size = size * scale,
                    Alignment = TextAlignment.CENTER,
                });
                
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "CursorWaitCursor", // weird, I know, but windows uses WaitCursor and I have the prefix Cursor
                    Position = position + new Vector2(size.X*scale/4, -size.Y*scale/8),
                    Size = size * scale/2,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = rotation,
                });
                
                
                return;
            }
            sprites.Add( new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = $"Cursor{cursor}",
                Position = position,
                Size = size * scale,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = rotation,
            });
        }

        public static void DebugDrawAllCursor(List<MySprite> sprites, Vector2 position, Vector2 size, float scale = 1)
        {
            CursorType type = CursorType.None;
            while (type <= CursorType.No)
            {
                AddCursor(sprites, type, position, size, scale);
                type++;
                position = new Vector2(position.X+ size.X, position.Y);
            }
        }
    }
}