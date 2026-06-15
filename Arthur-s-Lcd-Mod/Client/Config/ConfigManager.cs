using System;
using System.Collections.Generic;
using System.Linq;
using Generated;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;


namespace LcdMod.Client.Config
{
    /// <summary>
    /// Ensures settings is correctly Saved/Loaded and Synced between clients
    /// </summary>
    public static class ConfigManager
    {
        static readonly IConfigGenerator ConfigGenerator = new ConfigGenerator();
        
        public static ScreenConfigGeneral GenerateConfig(int configId) => ConfigGenerator.GenerateConfig((ConfigKind)configId) as ScreenConfigGeneral;

        public static void SaveAll()
        {
            try
            {
                foreach (var screen in SurfaceScriptBase.Instances)
                    Save(screen.Block, screen.ProviderConfig);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(ConfigManager));
            }
        }

        public static void SyncAll()
        {
            try
            {
                foreach (var screen in SurfaceScriptBase.Instances)
                    Sync(screen.Block, screen.ProviderConfig);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(ConfigManager));
            }
        }

        public static void Save(IMyEntity storageEntity, ScreenProviderConfig providerConfig)
        {
            ScreenProviderConfigStorage.Save(storageEntity, providerConfig);
        }

        public static void Sync(IMyEntity storageEntity, ScreenProviderConfig providerConfig)
        {
            foreach (var app in GetAppsForBlock(storageEntity as IMyTerminalBlock))
                app.UseProviderConfig(providerConfig);

            foreach (var app in GetAppsForBlock(storageEntity as IMyTerminalBlock)) 
                app.RequestRedraw();

            LcdModSessionComponent.NetworkManager.TransmitToServer(new NetworkPackageSyncScreenConfig(storageEntity.EntityId, providerConfig));
            Save(storageEntity, providerConfig);
        }

        public static void Sync(IMyTerminalBlock storageEntity) =>
            Sync(storageEntity, GetConfigForBlock(storageEntity));

        public static void LoadSettings(IMyCubeBlock block, int index, ConfigKind requestedConfigKind, ref ScreenProviderConfig provider,
            out ScreenConfigGeneral screen)
        {
            try
            {
                provider = GetConfigForBlock((IMyTerminalBlock)block);
                if (provider != null && provider.Screens.Count > index)
                {
                    screen = EnsureScreenConfigType(provider, index, requestedConfigKind);
                    provider.BindRuntimeParent((IMyTerminalBlock)block);
                    return;
                }

                var storageEntity = (IMyEntity)block;

                if (storageEntity.Storage == null)
                    storageEntity.Storage = new MyModStorageComponent();

                provider = TryLoad(block);
                if (provider != null)
                {
                    screen = EnsureScreenConfigType(provider, index, requestedConfigKind);
                    provider.BindRuntimeParent((IMyTerminalBlock)block);
                    return;
                }

                CreateSettings(block, index, requestedConfigKind, out provider, out screen);
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification($"Fail to Load Settings for block {block.DisplayNameText}\n{e.Message}");
                ErrorHandlerHelper.LogError(e, typeof(ConfigManager));
                CreateSettings(block, index, requestedConfigKind, out provider, out screen);
            }
        }

        public static ScreenProviderConfig TryLoad(IMyCubeBlock block)
        {
            var provider = ScreenProviderConfigStorage.TryLoad(block);
            if (provider != null)
            {
                var terminalBlock = (IMyTerminalBlock)block;

                if (provider.Parent != block.CubeGrid.EntityId)
                    provider.SetParent(block);
                else
                    provider.BindRuntimeParent(terminalBlock);
                
            }

            return provider;
        }

        public static void CreateSettings(IMyCubeBlock block, int index, ConfigKind requestedConfigKind, out ScreenProviderConfig provider, out ScreenConfigGeneral screen)
        {
            provider = CreateSettings(block);
            screen = EnsureScreenConfigType(provider, index, requestedConfigKind);
            provider.BindRuntimeParent(block as IMyTerminalBlock);
            (screen as ScreenConfigColorable)?.ResetDefaultColors();
            Save(block, provider);
        }

        public static ScreenProviderConfig CreateSettings(IMyCubeBlock block) => new ScreenProviderConfig(block is IMyTextPanel ? 1 : ((IMyTextSurfaceProvider)block).SurfaceCount, block as IMyTerminalBlock);

        public static IEnumerable<SurfaceScriptBase> GetAppsForBlock(IMyTerminalBlock block) =>
            SurfaceScriptBase.Instances.Where(a => a.Block.Equals(block));
        
        public static ScreenProviderConfig GetConfigForBlock(IMyTerminalBlock block) =>
            GetAppsForBlock(block).FirstOrDefault()?.ProviderConfig;

        public static ScreenConfigGeneral GetConfigForScreen(IMyTerminalBlock block, int index)
        {
            var settings = GetConfigForBlock(block);

            if (settings?.Screens == null
                || settings.Screens.Count <= index
                || index < 0)
                return null;

            return settings.Screens[index];
        }

        public static ScreenConfigInteractive GetConfigForCurrentScreen(IMyTerminalBlock block) =>
            GetConfigForScreen(block, GetThisSurfaceIndex(block)) as ScreenConfigInteractive;

        static ScreenConfigGeneral EnsureScreenConfigType(ScreenProviderConfig provider, int index, ConfigKind requestedConfigKind)
        {
            var current = provider.Screens[index];
            var requested = ConfigGenerator.GenerateConfig(requestedConfigKind) as ScreenConfigGeneral;

            if (requested == null)
                return current;

            if (current == null)
            {
                provider.Screens[index] = requested;
                return requested;
            }

            if (current.GetType() == requested.GetType())
                return current;

            if (requestedConfigKind == ConfigKind.Interactive && current is ScreenConfigInteractive)
                return current;

            requested.Clone(current);
            provider.Screens[index] = requested;
            return requested;
        }

        public static int GetThisSurfaceIndex(IMyTerminalBlock block)
        {
            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            return multiTextPanel?.SelectedPanelIndex ?? 0;
        }
    }
}
