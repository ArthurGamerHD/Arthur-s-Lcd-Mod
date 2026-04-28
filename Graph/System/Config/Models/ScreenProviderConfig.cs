using System;
using System.Collections.Generic;
using System.Linq;
using Graph.System.Config.Models.Apps;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Graph.System.Config.Models
{
    [ProtoContract]
    public class ScreenProviderConfig
    {
        public static Version CurrentVersion => new Version(0, 1);
        
        public ScreenProviderConfig() 
        {
            //Required by Protobuf
        }

        public ScreenProviderConfig(int surfaceCount, IMyTerminalBlock parent)
        {
            Screens = new List<ScreenConfigGeneral>();

            for (int i = 0; i < surfaceCount; i++)
                Screens.Add(new ScreenConfigGeneral(i, parent));

            Parent = parent.CubeGrid.EntityId;
        }

        [ProtoMember(1)] public List<ScreenConfigGeneral> Screens { get; set; }

        [ProtoMember(2)] public long Parent { get; set; }

        public void BindRuntimeParent(IMyTerminalBlock block)
        {
            if (Screens == null)
                return;

            foreach (var providerScreen in Screens)
            {
                if (providerScreen != null)
                    providerScreen.ParentBlock = block;
            }
        }

        public void CopyFrom(ScreenProviderConfig other)
        {
            if (Screens.Count != other.Screens.Count)
            {
                MyLog.Default.WriteLine(
                    $"[LCDMod] CopyFrom: Screens count mismatch ({Screens.Count} vs {other.Screens.Count}), rebuilding list.");
                Screens.Clear();
                for (int i = 0; i < other.Screens.Count; i++)
                    Screens.Add(new ScreenConfigGeneral());
            }

            for (var index = 0; index < Screens.Count; index++)
            {
                var source = other.Screens[index];
                var target = Screens[index];

                if (source == null)
                {
                    Screens[index] = new ScreenConfigGeneral();
                    continue;
                }

                if (target == null || target.GetType() != source.GetType())
                {
                    target = ConfigManager.GenerateConfig(source.Id);
                    target.Clone(source);
                    Screens[index] = target;
                    continue;
                }

                target.Clone(source);
                Screens[index] = target;
            }
        }

        public void SetParent(IMyCubeBlock block)
        {
            Parent = block.CubeGrid.EntityId;

            BindRuntimeParent((IMyTerminalBlock)block);
            
            // todo: Some Extra logic is Required to properly migrate blocks ids when creating Blueprints
            foreach (var s in Screens.OfType<ScreenConfigWithBlocks>()) s.SelectedBlocks = Array.Empty<long>();
        }
    }
}
