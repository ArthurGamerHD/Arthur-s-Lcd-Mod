using System;
using LcdMod.Client.Images;
using System.Collections.Generic;
using System.Linq;
using Generated;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Constants = LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Config
{
    /// <summary>
    /// Saves and synchronizes the component graph. Running apps read the exact components they
    /// consume directly from each SurfaceConfig.
    /// </summary>
    public static class ConfigManager
    {
        public static SurfaceConfig GenerateSurfaceConfig(AppType appType, int surfaceIndex)
        {
            return AppSchemaRegistry.CreateSurface(appType, surfaceIndex);
        }

        public static void SaveAll()
        {
            try
            {
                foreach (var group in SurfaceScriptBase.Instances
                             .Where(screen => screen?.Block != null && screen.ProviderConfig != null)
                             .GroupBy(screen => screen.Block.EntityId))
                {
                    var screen = group.First();
                    Save(screen.Block, screen.ProviderConfig);
                }
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(ConfigManager));
            }
        }

        public static void SyncAll()
        {
            try
            {
                foreach (var group in SurfaceScriptBase.Instances
                             .Where(screen => screen?.Block != null && screen.ProviderConfig != null)
                             .GroupBy(screen => screen.Block.EntityId))
                {
                    var screen = group.First();
                    Sync(screen.Block, screen.ProviderConfig);
                }
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(ConfigManager));
            }
        }

        public static void Save(IMyEntity storageEntity, ScreenProviderConfig providerConfig)
        {
            ScreenProviderConfigStorage.Save(storageEntity, providerConfig);
        }

        public static void Sync(IMyEntity storageEntity, ScreenProviderConfig providerConfig)
        {
            if (storageEntity == null || providerConfig == null || !providerConfig.CanWrite)
                return;
            if (!providerConfig.NormalizeComponentSchema())
                return;

            var block = storageEntity as IMyTerminalBlock;
            var apps = GetAppsForBlock(block).ToArray();
            foreach (var app in apps)
                app.UseProviderConfig(providerConfig);
            foreach (var app in apps)
                app.RequestRedraw();

            GridImageClientService.Instance?.UploadReferenced(block, providerConfig);
            LcdModSessionComponent.NetworkManager.TransmitToServer(
                new NetworkPackageSyncComponentConfig(storageEntity.EntityId, providerConfig));
            Save(storageEntity, providerConfig);
        }

        public static void Sync(IMyTerminalBlock storageEntity)
        {
            Sync(storageEntity, GetConfigForBlock(storageEntity));
        }

        public static void LoadSettings(
            IMyCubeBlock block,
            int index,
            AppType requestedAppType,
            ref ScreenProviderConfig provider,
            out SurfaceConfig screen)
        {
            try
            {
                provider = GetConfigForBlock((IMyTerminalBlock)block);
                if (provider != null)
                {
                    screen = ResolveSurfaceConfig(provider, index, requestedAppType);
                    return;
                }

                var storageEntity = (IMyEntity)block;
                if (storageEntity.Storage == null)
                    storageEntity.Storage = new MyModStorageComponent();

                provider = TryLoad(block);
                if (provider != null)
                {
                    screen = ResolveSurfaceConfig(provider, index, requestedAppType);
                    return;
                }

                CreateSettings(block, index, requestedAppType, out provider, out screen);
            }
            catch (Exception exception)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    $"Fail to Load Settings for block {block.DisplayNameText}\n{exception.Message}");
                ErrorHandlerHelper.LogError(exception, typeof(ConfigManager));
                CreateSettings(block, index, requestedAppType, out provider, out screen);
            }
        }

        public static ScreenProviderConfig TryLoad(IMyCubeBlock block)
        {
            var provider = ScreenProviderConfigStorage.TryLoad(block);
            if (provider != null && provider.CanWrite && provider.Parent != block.CubeGrid.EntityId)
                provider.SetParent(block);
            return provider;
        }

        public static void CreateSettings(
            IMyCubeBlock block,
            int index,
            AppType requestedAppType,
            out ScreenProviderConfig provider,
            out SurfaceConfig screen)
        {
            provider = CreateSettings(block);
            screen = ResolveSurfaceConfig(provider, index, requestedAppType);
            var colors = screen?.TryGetComponent<ColorConfigComponent>();
            if (colors != null)
            {
                colors.HeaderColor.Clear();
                colors.ErrorColor.Clear();
                colors.WarningColor.Clear();
            }
            Save(block, provider);
        }

        public static ScreenProviderConfig CreateSettings(IMyCubeBlock block)
        {
            return new ScreenProviderConfig(
                block is IMyTextPanel ? 1 : ((IMyTextSurfaceProvider)block).SurfaceCount,
                block as IMyTerminalBlock);
        }

        public static IEnumerable<SurfaceScriptBase> GetAppsForBlock(IMyTerminalBlock block)
        {
            return block == null
                ? Enumerable.Empty<SurfaceScriptBase>()
                : SurfaceScriptBase.Instances.Where(app => app.Block.Equals(block));
        }

        public static ScreenProviderConfig GetConfigForBlock(IMyTerminalBlock block)
        {
            return GetAppsForBlock(block).FirstOrDefault()?.ProviderConfig;
        }

        public static SurfaceConfig GetConfigForScreen(IMyTerminalBlock block, int index)
        {
            var settings = GetConfigForBlock(block);
            if (settings == null || index < 0)
                return null;

            var surface = settings.GetSurfaceConfig(index);
            if (surface == null)
                return null;
            var runtimeSurface = settings.CanWriteConfig(surface) ? surface : surface.Clone();
            return runtimeSurface;
        }

        public static SurfaceConfig GetConfigForCurrentScreen(IMyTerminalBlock block)
        {
            return GetConfigForScreen(block, GetThisSurfaceIndex(block));
        }

        static SurfaceConfig ResolveSurfaceConfig(
            ScreenProviderConfig provider,
            int index,
            AppType requestedAppType)
        {
            if (provider == null || index < 0)
                return null;

            var surface = EnsureSurfaceApp(provider, index, requestedAppType);
            if (surface == null)
                return null;
            var runtimeSurface = provider.CanWriteConfig(surface) ? surface : surface.Clone();
            return runtimeSurface;
        }

        public static SurfaceConfig GetSurfaceConfigForCurrentScreen(IMyTerminalBlock block)
        {
            var settings = GetConfigForBlock(block);
            return settings == null ? null : settings.GetSurfaceConfig(GetThisSurfaceIndex(block));
        }

        static IMyTerminalBlock _nestedBlock;
        static IAppConfig _nestedConfig;
        static string _nestedScript;

        /// <summary>
        /// Runs terminal control code against an app hosted inside the block's surface (a multi-function display page or
        /// window) instead of the surface's own app: "current surface" lookups for that block resolve to the nested config.
        /// </summary>
        public static void RunNested(IMyTerminalBlock block, IAppConfig config, string script, Action action)
        {
            var previousBlock = _nestedBlock;
            var previousConfig = _nestedConfig;
            var previousScript = _nestedScript;
            _nestedBlock = block;
            _nestedConfig = config;
            _nestedScript = script;
            try
            {
                action();
            }
            finally
            {
                _nestedBlock = previousBlock;
                _nestedConfig = previousConfig;
                _nestedScript = previousScript;
            }
        }

        static IAppConfig NestedConfig(IMyTerminalBlock block)
        {
            return _nestedConfig != null && ReferenceEquals(block, _nestedBlock) ? _nestedConfig : null;
        }

        /// <summary>
        /// The script of the app terminal controls edit for the block's selected surface: the surface's own script, or
        /// the hosted app's in <see cref="RunNested"/>.
        /// </summary>
        public static string GetTerminalScript(IMyTerminalBlock block, int index)
        {
            if (_nestedConfig != null && ReferenceEquals(block, _nestedBlock))
                return _nestedScript;
            var provider = block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
            if (provider == null || index < 0 || index >= provider.SurfaceCount)
                return null;
            var surface = provider.GetSurface(index);
            return surface == null ? null : surface.Script;
        }

        /// <summary>The config terminal controls edit for the block's selected surface, or the nested one in <see cref="RunNested"/>.</summary>
        public static IAppConfig GetTerminalConfig(ScreenProviderConfig settings, IMyTerminalBlock block, int index)
        {
            return NestedConfig(block) ?? (settings == null ? null : settings.GetSurfaceConfig(index));
        }

        public static T GetComponentForTerminalApp<T>(IMyTerminalBlock block) where T : ConfigComponent
        {
            return GetComponentForCurrentSurface<T>(block, Constants.APP);
        }

        public static T GetComponentForCurrentSurface<T>(IMyTerminalBlock block, string slot) where T : ConfigComponent
        {
            var nested = NestedConfig(block);
            if (nested != null)
                return nested.TryGet<T>(slot);
            var surface = GetSurfaceConfigForCurrentScreen(block);
            return surface?.TryGet<T>(slot);
        }

        public static bool ModifyComponentForTerminalApp<T>(IMyTerminalBlock block, Action<T> action)
            where T : ConfigComponent
        {
            return ModifyComponentForCurrentSurface(block, Constants.APP, action);
        }

        public static bool ModifyComponentForCurrentSurface<T>(IMyTerminalBlock block, string slot, Action<T> action)
            where T : ConfigComponent
        {
            if (block == null || action == null)
                return false;

            var settings = GetConfigForBlock(block);
            if (settings == null || !settings.CanWrite)
                return false;

            var surface = GetTerminalConfig(settings, block, GetThisSurfaceIndex(block));
            if (!settings.CanWriteConfig(surface))
                return false;

            var component = surface?.TryGet<T>(slot);
            if (component == null)
                return false;

            action(component);
            Sync(block, settings);
            return true;
        }

        public static SurfaceConfig EnsureSurfaceApp(
            ScreenProviderConfig provider,
            int index,
            AppType requestedAppType)
        {
            if (provider == null)
                return null;

            provider.EnsureSurfaceApp(index, requestedAppType);
            return provider.GetSurfaceConfig(index);
        }

        public static int GetThisSurfaceIndex(IMyTerminalBlock block)
        {
            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            return multiTextPanel?.SelectedPanelIndex ?? 0;
        }
    }
}
