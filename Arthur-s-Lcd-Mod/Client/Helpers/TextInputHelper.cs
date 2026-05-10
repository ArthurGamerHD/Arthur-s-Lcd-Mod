using System;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal;
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

namespace LcdMod.Client.Helpers
{
    public static class TextInputHelper
    {
        static readonly MyDefinitionId CornerLcdId =
            new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), "SmallBlockCorner_LCD_Flat_1");

        static TextInputLcd _clientTextInput;

        static Action<string> _currentCallback;

        static string _currentTitle = string.Empty;

        public static void SpawnForLocalPlayer(string title, Action<string> callback, int lifetimeTicks = -1)
        {
            IMyPlayer player = MyAPIGateway.Session?.LocalHumanPlayer;
            if (player == null)
                return;

            if (TerminalManager.ShowTextPanelButton == null)
            {
                MyAPIGateway.Utilities.ShowNotification("LcdMod_ShowTextPanel_Action_Missing");
                return;
            }

            _currentTitle = title;
            _currentCallback = callback;

            long playerId = player.IdentityId;

            if (_clientTextInput != null)
            {
                // this should never happen on a normal situation, but if the user is laggy,
                // reuse the same block instead of creating a new one 
                OpenTextBox(_clientTextInput, _currentTitle);
                return;
            }

            
            if (MyAPIGateway.Multiplayer.MultiplayerActive && !MyAPIGateway.Multiplayer.IsServer)
                LcdModSessionComponent.NetworkManager?.TransmitToServer(
                    new PacketTextInputHelper(playerId, GhostLcdAction.Spawn, lifetimeTicks), false, sendToSender: true);
            else
            {
                SpawnInternal(player.Character, playerId, lifetimeTicks, 0,
                    ghost =>
                    {
                        _clientTextInput = ghost;
                        MyAPIGateway.Utilities.ShowNotification(
                            $"block spawned at {ghost.Grid.PositionComp.GetPosition()}");
                        OpenTextBox(ghost, title);
                    });
            }
        }

        public static void SpawnFromRemotePlayer(PacketTextInputHelper package)
        {
            IMyPlayer player = MyAPIGateway.Session?.LocalHumanPlayer;
            if (player == null)
                return;
            
            SpawnInternal(player.Character, player.IdentityId, package.LifetimeTicks, package.GridId,
                ghost =>
                {
                    _clientTextInput = ghost;
                    OpenTextBox(ghost, _currentTitle);
                });
        }

        static void OpenTextBox(TextInputLcd textInput, string title)
        {
            textInput.Lcd?.WritePublicTitle(title);
            TerminalManager.ShowTextPanelButton.Action(textInput.Lcd);


            LcdModClientComponent.RunNextFrame.Add(CheckIfIsOpened);
        }

        static void CheckIfIsOpened()
        {
            if (MyAPIGateway.Gui.IsCursorVisible)
            {
                LcdModClientComponent.RunNextFrame.Add(CheckIfIsOpened);
                return; // user still with the textbox opened
            }

            if (_clientTextInput.Lcd == null)
                return;

            if (_clientTextInput.Lcd.WriteText("dummy"))
            {
                _currentCallback(_clientTextInput.Lcd.GetText());
                _clientTextInput.Close();
                _clientTextInput = null;
            }
            else // game still thinks the Lcd is being edited
            {
                LcdModClientComponent.RunNextFrame.Add(CheckIfIsOpened);
            }
        }

        public static void ClientUpdate()
        {
            if (_clientTextInput != null)
            {
                _clientTextInput.Update();
                LcdModClientComponent.RunNextFrame.Add(ClientUpdate);
            }
        }

        public static void ClientClear()
        {
            _clientTextInput?.Close();
            _clientTextInput = null;
        }

        static void SpawnInternal(
            IMyCharacter character,
            long playerId,
            int lifetimeTicks,
            long gridId,
            Action<TextInputLcd> onSpawned)
        {
            if (character == null)
            {
                MyAPIGateway.Utilities.ShowNotification($"character was null");
                return;
            }

            var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(CornerLcdId);
            if (definition == null)
            {
                MyAPIGateway.Utilities.ShowNotification($"CornerLcd definition was null");
                return;
            }


            var blockBuilder = (MyObjectBuilder_CubeBlock)MyObjectBuilderSerializer.CreateNewObject(definition.Id);
            blockBuilder.BuildPercent = 1f;
            blockBuilder.IntegrityPercent = 1f;
            blockBuilder.Min = Vector3I.Zero;
            blockBuilder.BlockOrientation = MyBlockOrientation.Identity;
            if (gridId == 0)
                gridId = MyRandom.Instance.NextLong() & 72057594037927935L;
            
            blockBuilder.EntityId = gridId;
            var gridBuilder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            gridBuilder.GridSizeEnum = definition.CubeSize;
            gridBuilder.IsStatic = false;
            gridBuilder.Editable = false;
            gridBuilder.DestructibleBlocks = false;
            gridBuilder.CubeBlocks.Add(blockBuilder);
            gridBuilder.PositionAndOrientation = new MyPositionAndOrientation(character.WorldMatrix);

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
                entity.Render.Visible = false;

                var grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    MyAPIGateway.Utilities.ShowNotification($"grid was null");
                    return;
                }

                grid.CustomName = $"LCDMod_TextInputForPlayer{character.DisplayName}";

                onSpawned(new TextInputLcd(grid, character, playerId, lifetimeTicks));
            });
            
            LcdModClientComponent.RunNextFrame.Add(ClientUpdate);
        }
    }
}