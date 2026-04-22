using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Graph.System.Config
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
            Screens = new List<ScreenConfig>();

            for (int i = 0; i < surfaceCount; i++)
                Screens.Add(new ScreenConfig(i, parent));

            Parent = parent.CubeGrid.EntityId;
        }

        [ProtoMember(1)] public List<ScreenConfig> Screens { get; set; }

        [ProtoMember(2)] public long Parent { get; set; }

        public void CopyFrom(ScreenProviderConfig other)
        {
            if (Screens.Count != other.Screens.Count)
            {
                MyLog.Default.WriteLine(
                    $"[LCDMod] CopyFrom: Screens count mismatch ({Screens.Count} vs {other.Screens.Count}), rebuilding list.");
                Screens.Clear();
                for (int i = 0; i < other.Screens.Count; i++)
                    Screens.Add(new ScreenConfig());
            }

            for (var index = 0; index < Screens.Count; index++)
                Screens[index].CopyFrom(other.Screens[index]);
        }

        public void SetParent(long value)
        {
            Parent = value;

            // todo: Some Extra logic is Required to properly migrate blocks ids when creating Blueprints
            Screens?.ForEach(s => s.SelectedBlocks = Array.Empty<long>()); // fail-safe deleting outdated ID's
        }
    }
}