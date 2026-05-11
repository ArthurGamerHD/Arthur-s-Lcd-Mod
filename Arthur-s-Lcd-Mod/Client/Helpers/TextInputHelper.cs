using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    public static class TextInputHelper
    {
        static bool _wasOpened;

        static readonly MyDefinitionId CockpitId =
            new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), "SmallBlockCockpit");
        static MyCockpitDefinition _cockpitDefinition;
        static string _originalLcdDisplayName;

        static TextInputModel _clientTextInput;

        static Action<string> _currentCallback;

        static string _currentTitle = string.Empty;
        static string _currentSubTitle = string.Empty;
        static string _initialText = string.Empty;

        public static void SpawnForLocalPlayer(
            string title,
            Action<string> callback,
            string initialText = "",
            string subtitle = "")
        {
            _currentTitle = title;
            _currentSubTitle = subtitle;
            _initialText = initialText ?? string.Empty;
            _currentCallback = callback;
            
            _clientTextInput?.Close();

            // Cockpit.OpenWindow() lets this stay purely client-side.
            SpawnInternal(OpenTextBox);
        }
        
        public static string GetSerializedText(int surfaceIndex = 0)
        {
            if (_clientTextInput.Grid == null)
                return string.Empty;

            var slimBlock = _clientTextInput.Grid.GetCubeBlock(Vector3I.Zero);
            var fatBlock = slimBlock?.FatBlock;
            if (fatBlock == null)
                return string.Empty;

            var blockBuilder = fatBlock.GetObjectBuilderCubeBlock(true);
            if (blockBuilder?.ComponentContainer?.Components == null)
                return string.Empty;

            foreach (var componentData in blockBuilder.ComponentContainer.Components)
            {
                if (componentData.TypeId != "MyMultiTextPanelComponent")
                    continue;

                var multiTextBuilder =
                    componentData.Component as MyObjectBuilder_MultiTextPanelComponent;

                if (multiTextBuilder?.TextPanelsContents == null)
                    return string.Empty;

                if (surfaceIndex < 0 || surfaceIndex >= multiTextBuilder.TextPanelsContents.Count)
                    return string.Empty;

                return multiTextBuilder.TextPanelsContents[surfaceIndex].Text ?? string.Empty;
            }

            return string.Empty;
        }

        static void OpenTextBox(TextInputModel textInputModel)
        {
            _clientTextInput?.Close();
            _clientTextInput = textInputModel;
            
            if (_clientTextInput?.Cockpit == null || _clientTextInput.Lcd == null)
                return;
            
            _clientTextInput.Cockpit.OpenWindow(true, false, true);
            LcdModClientComponent.RunNextFrame.Add(CheckIfIsOpened);
        }

        static void RestoreLcdName() => _cockpitDefinition.ScreenAreas[0].DisplayName = _originalLcdDisplayName;

        static void CheckIfIsOpened()
        {
            var isOpen = MyAPIGateway.Gui.IsCursorVisible;
            
            if (isOpen || !_wasOpened)
            {
                _wasOpened = isOpen;
                LcdModClientComponent.RunNextFrame.Add(CheckIfIsOpened);
                return; // user still has the textbox opened
            }
            
            _wasOpened = false;
            var text = GetSerializedText();
            RestoreLcdName();
            _currentCallback?.Invoke(text);
            _clientTextInput.Close();
            _clientTextInput = null;
        }

        static void SpawnInternal(Action<TextInputModel> onSpawned)
        {
            if (_cockpitDefinition == null)
            {
                _cockpitDefinition = (MyCockpitDefinition)MyDefinitionManager.Static.GetCubeBlockDefinition(CockpitId);
                if (_cockpitDefinition == null)
                {
                    MyAPIGateway.Utilities.ShowNotification("Cockpit definition was null");
                    return;
                }

                _originalLcdDisplayName = _cockpitDefinition.ScreenAreas[0].DisplayName;
            }

            _cockpitDefinition.ScreenAreas[0].DisplayName = _currentSubTitle;

            var blockBuilder = (MyObjectBuilder_Cockpit)
                MyObjectBuilderSerializer.CreateNewObject(_cockpitDefinition.Id);
            
            
            var multiTextBuilder =
                MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_MultiTextPanelComponent>();

            multiTextBuilder.TextPanelsContents = new List<MySerializedTextPanelData>
            {
                new MySerializedTextPanelData
                {
                    Text = _initialText ?? string.Empty
                }
            };

            blockBuilder.BuildPercent = 1f;
            blockBuilder.IntegrityPercent = 1f;
            blockBuilder.Min = Vector3I.Zero;
            blockBuilder.BlockOrientation = MyBlockOrientation.Identity;
            blockBuilder.CustomName = string.IsNullOrEmpty(_currentTitle)
                ? "notepad.exe"
                : _currentTitle;

            blockBuilder.ComponentContainer = new MyObjectBuilder_ComponentContainer();
            blockBuilder.ComponentContainer.Components.Add(
                new MyObjectBuilder_ComponentContainer.ComponentData
                {
                    TypeId = "MyMultiTextPanelComponent",
                    Component = multiTextBuilder
                });

            var gridBuilder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            gridBuilder.GridSizeEnum = _cockpitDefinition.CubeSize;
            gridBuilder.IsStatic = false;
            gridBuilder.Editable = false;
            gridBuilder.DestructibleBlocks = false;
            gridBuilder.CubeBlocks.Add(blockBuilder);
            var matrix = MyAPIGateway.Session?.LocalHumanPlayer?.Character?.PositionComp?.WorldMatrixRef;
            gridBuilder.PositionAndOrientation = new MyPositionAndOrientation(matrix ?? MatrixD.Identity);

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(gridBuilder);
                if (entity == null)
                {
                    MyAPIGateway.Utilities.ShowNotification("entity was null");
                    return;
                }

                entity.Synchronized = false;
                entity.StopPhysicsActivation = true;
                entity.Save = false;
                entity.Render.Visible = false;

                var grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    MyAPIGateway.Utilities.ShowNotification("grid was null");
                    return;
                }

                grid.CustomName = "LCDMod_TextInputGrid";

                onSpawned(new TextInputModel(grid, grid.GetCubeBlock(Vector3I.Zero)?.FatBlock as MyCockpit));
            });
        }
        
        public sealed class TextInputModel
        {
            public IMyCubeGrid Grid { get; private set; }
            public MyCockpit Cockpit { get; private set; }
            public IMyTextSurface Lcd { get; private set; }

            public TextInputModel(IMyCubeGrid grid, MyCockpit cockpit)
            {
                Grid = grid;
                Cockpit = cockpit;
                Lcd = (IMyTextSurface)((IMyCockpit)Cockpit)?.GetSurface(0);
                Grid.OnMarkForClose += OnGridMarkedForClose;
                Update();
            }

            public void Update()
            {
                if (Grid != null)
                    LcdModClientComponent.RunNextFrame.Add(Update);

                var position = MyAPIGateway.Session?.LocalHumanPlayer?.GetPosition();
                if(position != null && Grid != null)
                    Grid.SetPosition(position.Value);
            }
            
            public void Close()
            {
                if (Grid == null)
                    return;

                Grid.OnMarkForClose -= OnGridMarkedForClose;

                if (!Grid.MarkedForClose)
                    Grid.Close();

                Grid = null;
                Cockpit = null;
                Lcd = null;
            }

            void OnGridMarkedForClose(IMyEntity _)
            {
                if (Grid == null)
                    return;
            
                Grid.OnMarkForClose -= OnGridMarkedForClose;

                Grid = null;
                Cockpit = null;
                Lcd = null;
            }
        }
    }
}