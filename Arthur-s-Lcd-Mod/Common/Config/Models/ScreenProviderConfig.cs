using System;
using System.Collections.Generic;
using System.Linq;
using Generated;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    public class ScreenProviderConfig
    {
        static readonly IConfigGenerator ConfigGenerator = new ConfigGenerator();

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

        public ScreenProviderConfig CopyFrom(ScreenProviderConfig other)
        {
            if (Screens.Count != other.Screens.Count)
            {
                MyLog.Default.WriteLine(
                    $"[LcdMod] CopyFrom: Screens count mismatch ({Screens.Count} vs {other.Screens.Count}), rebuilding list.");
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
                    target = ConfigGenerator.GenerateConfig((ConfigKind)source.Id) as ScreenConfigGeneral;
                }

                if (target == null) continue;
                target.Clone(source);
                Screens[index] = target;
            }
            
            return this;
        }

        public void SetParent(IMyCubeBlock block)
        {
            Parent = block.CubeGrid.EntityId;
            BindRuntimeParent((IMyTerminalBlock)block);
            foreach (var s in Screens.OfType<ScreenConfigWithBlocks>()) s.SelectedBlocks = Array.Empty<long>();
        }
    }
}
