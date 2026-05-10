using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using LcdMod.Common.Terminal;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRageMath;

namespace LcdMod.Server.Helpers
{
    public static class TextInputHelper
    {
        static readonly MyDefinitionId CornerLcdId = new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "SmallBlockCorner_LCD_Flat_1");
        
        static readonly Dictionary<long, TextInputLcd> TextInputs = new Dictionary<long, TextInputLcd>();
        
        public static void SpawnForPlayer(Action<long> callback, long sender, int lifetimeTicks = -1)
        {
            TextInputLcd textInput;
            if (TextInputs.TryGetValue(lifetimeTicks, out textInput)) 
                textInput.Close();
            
            SpawnInternal(sender, lifetimeTicks,
                ghost =>
                {
                    TextInputs[sender] = ghost;
                    callback(ghost.Grid.EntityId);
                });
        }
        
        static void SpawnInternal(
            long playerId,
            int lifetimeTicks,
            Action<TextInputLcd> onSpawned)
        {
            var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(CornerLcdId);
            if (definition == null)
            {
                LogHelper.LogInfo($"CornerLcd definition was null");
                return;
            }

            var character = MyAPIGateway.Players.TryGetIdentityId(playerId)?.Character;

            var blockBuilder = (MyObjectBuilder_CubeBlock)MyObjectBuilderSerializer.CreateNewObject(definition.Id);
            blockBuilder.BuildPercent = 1f;
            blockBuilder.IntegrityPercent = 1f;
            blockBuilder.Min = Vector3I.Zero;
            blockBuilder.BlockOrientation = MyBlockOrientation.Identity;
            blockBuilder.EntityId = MyRandom.Instance.NextLong() & 72057594037927935L;

            var gridBuilder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            gridBuilder.GridSizeEnum = definition.CubeSize;
            gridBuilder.IsStatic = false;
            gridBuilder.Editable = false;
            gridBuilder.DestructibleBlocks = false;
            gridBuilder.CubeBlocks.Add(blockBuilder);
            gridBuilder.PositionAndOrientation = new MyPositionAndOrientation(character?.WorldMatrix ?? MatrixD.Zero);

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(gridBuilder);
                if (entity == null)
                {
                    MyAPIGateway.Utilities.ShowNotification($"entity was null");
                    return;
                }

                entity.Synchronized = false;
                entity.StopPhysicsActivation = true;
                entity.Save = false;
                entity.Render.Visible = true; // false;

                var grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    MyAPIGateway.Utilities.ShowNotification($"grid was null");
                    return;
                }

                grid.CustomName = $"LCDMod_TextInputForPlayer{character?.DisplayName ?? playerId.ToString()}";

                onSpawned(new TextInputLcd(grid, character, playerId, lifetimeTicks));
            });
        }
    }
}