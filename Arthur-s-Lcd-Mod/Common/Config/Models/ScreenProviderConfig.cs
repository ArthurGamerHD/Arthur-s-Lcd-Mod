using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Generated;
using LcdMod.Common.Config.Components;
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

        public const int COMPONENT_SCHEMA_VERSION = 1;
        public static Version CurrentVersion => new Version(0, 2);

        public ScreenProviderConfig()
        {
            // Required by Protobuf.
        }

        public ScreenProviderConfig(int surfaceCount, IMyTerminalBlock parent)
        {
            Parent = parent.CubeGrid.EntityId;
            SchemaVersion = COMPONENT_SCHEMA_VERSION;

            for (var index = 0; index < surfaceCount; index++)
            {
                var runtime = new ScreenConfigGeneral(index, parent);
                Screens.Add(runtime);
                Surfaces.Add(ComponentConfigAdapter.FromRuntimeSurface(runtime, index));
            }
        }

        [ProtoMember(1)]
        public int SchemaVersion { get; set; } = COMPONENT_SCHEMA_VERSION;

        [ProtoMember(2)] public long Parent { get; set; }

        [ProtoMember(3)]
        [XmlArrayItem("Surface")]
        public List<SurfaceConfig> Surfaces { get; set; } = new List<SurfaceConfig>();

        /// <summary>
        /// Transitional runtime facade for apps that still consume the inherited config classes.
        /// It is reconstructed from, and captured back into, <see cref="Surfaces"/>.
        /// </summary>
        [ProtoIgnore]
        [XmlIgnore]
        public List<ScreenConfigGeneral> Screens { get; set; } = new List<ScreenConfigGeneral>();

        public void EnsureRuntimeScreens()
        {
            if (Surfaces == null)
                Surfaces = new List<SurfaceConfig>();

            if (Surfaces.Count == 0)
            {
                if (Screens == null)
                    Screens = new List<ScreenConfigGeneral>();
                return;
            }

            var maxIndex = -1;
            foreach (var surface in Surfaces)
            {
                if (surface != null && surface.SurfaceIndex > maxIndex)
                    maxIndex = surface.SurfaceIndex;
            }

            // Existing apps keep references to these runtime facade objects. Only rebuild after
            // protobuf/network deserialization (where Screens is empty), not on every access.
            if (Screens != null && Screens.Count > maxIndex)
                return;

            var rebuilt = new List<ScreenConfigGeneral>(Math.Max(0, maxIndex + 1));
            for (var index = 0; index <= maxIndex; index++)
                rebuilt.Add(new ScreenConfigGeneral { ScreenIndex = index });

            foreach (var surface in Surfaces)
            {
                if (surface == null || surface.SurfaceIndex < 0)
                    continue;

                while (rebuilt.Count <= surface.SurfaceIndex)
                    rebuilt.Add(new ScreenConfigGeneral { ScreenIndex = rebuilt.Count });

                rebuilt[surface.SurfaceIndex] =
                    ComponentConfigAdapter.ToRuntime(surface, surface.SurfaceIndex);
            }

            Screens = rebuilt;
        }

        public void CaptureRuntimeScreens()
        {
            if (Screens == null)
                return;

            if (Surfaces == null)
                Surfaces = new List<SurfaceConfig>();

            for (var index = 0; index < Screens.Count; index++)
                CaptureRuntimeScreen(index);

            SchemaVersion = COMPONENT_SCHEMA_VERSION;
        }

        public void CaptureRuntimeScreen(int index)
        {
            if (Screens == null || index < 0 || index >= Screens.Count)
                return;

            var surface = GetOrCreateSurface(index);
            ComponentConfigAdapter.CaptureRuntime(Screens[index], surface);
        }

        public SurfaceConfig GetSurfaceConfig(int surfaceIndex)
        {
            return Surfaces?.FirstOrDefault(candidate =>
                candidate != null && candidate.SurfaceIndex == surfaceIndex);
        }

        public ScreenConfigGeneral EnsureScreenConfigType(int index, ConfigKind requestedConfigKind)
        {
            EnsureRuntimeScreens();

            if (index < 0 || index >= Screens.Count)
                return null;

            var current = Screens[index];
            var requested = ConfigGenerator.GenerateConfig(requestedConfigKind) as ScreenConfigGeneral;

            if (requested == null)
                return current;

            requested.ScreenIndex = index;

            if (current != null && current.GetType() == requested.GetType())
                return current;

            if (requestedConfigKind == ConfigKind.Interactive && current is ScreenConfigInteractive)
                return current;

            CaptureRuntimeScreen(index);
            var sourceSurface = GetOrCreateSurface(index);
            var targetSurface = ComponentConfigAdapter.FromRuntimeSurface(requested, index);
            targetSurface.CopyCompatibleFrom(sourceSurface);

            // Keep the surface object stable; only its selected app schema and components change.
            sourceSurface.AppKind = targetSurface.AppKind;
            sourceSurface.Components = targetSurface.Components;

            var materialized = ComponentConfigAdapter.ToRuntime(sourceSurface, index);
            Screens[index] = materialized;
            return materialized;
        }

        public void BindRuntimeParent(IMyTerminalBlock block)
        {
            EnsureRuntimeScreens();

            foreach (var providerScreen in Screens)
            {
                if (providerScreen != null)
                    providerScreen.ParentBlock = block;
            }
        }

        public ScreenProviderConfig CopyFrom(ScreenProviderConfig other)
        {
            if (other == null)
                return this;

            other.EnsureRuntimeScreens();
            EnsureRuntimeScreens();

            Parent = other.Parent;
            SchemaVersion = other.SchemaVersion;

            if (Screens.Count != other.Screens.Count)
            {
                MyLog.Default.WriteLine(
                    $"[LcdMod] CopyFrom: Screens count mismatch ({Screens.Count} vs {other.Screens.Count}), rebuilding list.");
                Screens.Clear();
                for (var index = 0; index < other.Screens.Count; index++)
                    Screens.Add(new ScreenConfigGeneral());
            }

            for (var index = 0; index < Screens.Count; index++)
            {
                var source = other.Screens[index];
                var target = Screens[index];

                if (source == null)
                {
                    Screens[index] = new ScreenConfigGeneral { ScreenIndex = index };
                    continue;
                }

                if (target == null || target.GetType() != source.GetType())
                    target = ConfigGenerator.GenerateConfig((ConfigKind)source.Id) as ScreenConfigGeneral;

                if (target == null)
                    continue;

                target.Clone(source);
                target.ScreenIndex = index;
                Screens[index] = target;
            }

            Surfaces = other.Surfaces == null
                ? new List<SurfaceConfig>()
                : other.Surfaces.Where(surface => surface != null).Select(surface => surface.Clone()).ToList();

            SchemaVersion = COMPONENT_SCHEMA_VERSION;
            return this;
        }

        public void SetParent(IMyCubeBlock block)
        {
            Parent = block.CubeGrid.EntityId;
            BindRuntimeParent((IMyTerminalBlock)block);
            foreach (var screen in Screens.OfType<ScreenConfigWithBlocks>())
                screen.SelectedBlocks = Array.Empty<long>();
            CaptureRuntimeScreens();
        }

        SurfaceConfig GetOrCreateSurface(int index)
        {
            if (Surfaces == null)
                Surfaces = new List<SurfaceConfig>();

            var surface = Surfaces.FirstOrDefault(candidate =>
                candidate != null && candidate.SurfaceIndex == index);
            if (surface != null)
                return surface;

            surface = new SurfaceConfig { SurfaceIndex = index };
            Surfaces.Add(surface);
            return surface;
        }


    }
}
