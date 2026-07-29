// ReSharper disable RedundantUsingDirective
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Generated;
using LcdMod.Common.Config.Components;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    public class ScreenProviderConfig
    {
        public const int COMPONENT_SCHEMA_VERSION = 1;

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
                Surfaces.Add(new SurfaceConfig
                {
                    SurfaceIndex = index,
                    AppTypeId = 0,
                    Components = new List<ConfigComponentEntry>()
                });
            }
        }

        [ProtoMember(1)]
        public int SchemaVersion { get; set; } = COMPONENT_SCHEMA_VERSION;

        [ProtoMember(2)] public long Parent { get; set; }

        [ProtoMember(3)]
        [XmlArrayItem("Surface")]
        public List<SurfaceConfig> Surfaces { get; set; } = new List<SurfaceConfig>();

        [ProtoIgnore]
        [XmlIgnore]
        public bool IsReadOnly { get; private set; }

        [ProtoIgnore]
        [XmlIgnore]
        public bool CanWrite => !IsReadOnly && SchemaVersion <= COMPONENT_SCHEMA_VERSION;

        public SurfaceConfig GetSurfaceConfig(int surfaceIndex)
        {
            return Surfaces?.FirstOrDefault(candidate =>
                candidate != null && candidate.SurfaceIndex == surfaceIndex);
        }

        public SurfaceConfig GetOrCreateSurfaceConfig(int index)
        {
            var surface = GetSurfaceConfig(index);
            if (surface != null || IsReadOnly)
                return surface;

            if (Surfaces == null)
                Surfaces = new List<SurfaceConfig>();

            surface = new SurfaceConfig
            {
                SurfaceIndex = index,
                AppTypeId = 0,
                Components = new List<ConfigComponentEntry>()
            };
            Surfaces.Add(surface);
            return surface;
        }

        public void EnsureSurfaceApp(int index, AppType requestedAppType)
        {
            if (IsReadOnly)
                return;

            var surface = GetOrCreateSurfaceConfig(index);
            if (surface == null)
                return;

            if (surface.AppTypeId == 0)
            {
                // Bind-time migration: the concrete surface script supplies the identity that old
                // shared legacy schema-kind values could not encode.
                AppSchemaRegistry.ChangeApp(surface, requestedAppType);
                return;
            }

            AppType existingAppType;
            if (!AppSchemaRegistry.TryNormalizeAppType(surface.AppTypeId, out existingAppType))
            {
                // Unknown future/extension app identities are opaque to this build.
                return;
            }

            if (existingAppType == requestedAppType)
            {
                AppSchemaRegistry.EnsureSchema(surface, requestedAppType);
                surface.LegacyAppKind = 0;
                return;
            }

            AppSchemaRegistry.ChangeApp(surface, requestedAppType);
        }

        public bool CanWriteConfig(IAppConfig config)
        {
            return CanWrite && config != null && AppSchemaRegistry.IsKnownAppType(config.AppTypeId);
        }

        /// <summary>
        /// Repairs component-schema V1 configurations understood by this build. Public V0 storage
        /// is migrated separately, and its surfaces remain unresolved until their concrete surface
        /// script binds an AppType.
        /// </summary>
        public bool NormalizeComponentSchema()
        {
            IsReadOnly = false;
            if (SchemaVersion > COMPONENT_SCHEMA_VERSION)
            {
                IsReadOnly = true;
                return false;
            }

            // Component storage starts at V1. Public V0 uses a different storage GUID and
            // is converted by LegacyV0Migrator before reaching this normalization path.
            if (SchemaVersion <= 0)
                SchemaVersion = COMPONENT_SCHEMA_VERSION;

            if (Surfaces == null)
                Surfaces = new List<SurfaceConfig>();

            var seenSurfaceIndexes = new HashSet<int>();
            for (var i = Surfaces.Count - 1; i >= 0; i--)
            {
                var surface = Surfaces[i];
                if (surface == null)
                {
                    Surfaces.RemoveAt(i);
                    continue;
                }

                if (surface.SurfaceIndex < 0)
                    surface.SurfaceIndex = i;

                if (!seenSurfaceIndexes.Add(surface.SurfaceIndex))
                {
                    Surfaces.RemoveAt(i);
                    continue;
                }

                NormalizeSurface(surface);
            }

            Surfaces.Sort((left, right) => left.SurfaceIndex.CompareTo(right.SurfaceIndex));
            return true;
        }

        public ScreenProviderConfig CopyFrom(ScreenProviderConfig other)
        {
            if (other == null)
                return this;

            Parent = other.Parent;
            SchemaVersion = other.SchemaVersion;
            Surfaces = other.Surfaces == null
                ? new List<SurfaceConfig>()
                : other.Surfaces.Where(surface => surface != null).Select(surface => surface.Clone()).ToList();
            NormalizeComponentSchema();
            return this;
        }

        public void SetParent(IMyCubeBlock block)
        {
            if (block == null || IsReadOnly)
                return;

            Parent = block.CubeGrid.EntityId;
            ClearSelectedBlocks(Surfaces);
        }

        static void ClearSelectedBlocks(List<SurfaceConfig> surfaces)
        {
            if (surfaces == null)
                return;

            foreach (var surface in surfaces)
            {
                if (surface == null)
                    continue;
                if (surface.AppTypeId != 0 && !AppSchemaRegistry.IsKnownAppType(surface.AppTypeId))
                    continue;
                ClearSelectedBlocks(surface);
            }
        }

        static void ClearSelectedBlocks(IComponentContainer config)
        {
            if (config == null || config.Components == null)
                return;

            foreach (var entry in config.Components)
            {
                var blocks = entry == null ? null : entry.Value as BlockSelectionConfigComponent;
                if (blocks != null)
                    blocks.SelectedBlocks = Array.Empty<long>();

                var tabs = entry == null ? null : entry.Value as TabContainerConfigComponent;
                if (tabs == null || tabs.Apps == null)
                    continue;

                foreach (var app in tabs.Apps)
                    ClearSelectedBlocks(app);
            }
        }

        static void NormalizeSurface(SurfaceConfig surface)
        {
            AppType appType = default(AppType);
            var hasKnownAppType = surface.AppTypeId != 0;
            if (hasKnownAppType && !AppSchemaRegistry.TryNormalizeAppType(surface.AppTypeId, out appType))
                return;

            if (surface.Components == null)
                surface.Components = new List<ConfigComponentEntry>();

            if (hasKnownAppType)
                AppSchemaRegistry.EnsureSchema(surface, appType);

            // Nested app execution/identity remains deferred. Preserve its data and only repair the
            // container's stable instance IDs.
            foreach (var entry in surface.Components)
            {
                var tabs = entry == null ? null : entry.Value as TabContainerConfigComponent;
                if (tabs == null)
                    continue;
                tabs.NormalizeAppInstanceIds();
            }
        }
    }
}
