using System;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using LcdMod.Common.Migration;
using LcdMod.Common.Migration.Legacy.V0;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Common.Config
{
    public static class ScreenProviderConfigStorage
    {
        public static bool Save(IMyEntity storageEntity, ScreenProviderConfig providerConfig)
        {
            try
            {
                if (providerConfig == null || storageEntity == null)
                {
                    LogHelper.Log(MyLogSeverity.Warning, "Save call with invalid block");
                    return false;
                }

                if (!providerConfig.NormalizeComponentSchema())
                {
                    MyLog.Default.WriteLine(
                        $"[LcdMod] Refusing to overwrite unsupported component schema {providerConfig.SchemaVersion}; current schema is {ScreenProviderConfig.COMPONENT_SCHEMA_VERSION}.");
                    return false;
                }

                if (storageEntity.Storage == null)
                    storageEntity.Storage = new MyModStorageComponent();

                var base64 = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(providerConfig));
                if (string.IsNullOrEmpty(base64))
                    throw new Exception("Invalid component config");

                storageEntity.Storage[Constants.StorageGuid] = base64;
                return true;
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(ScreenProviderConfigStorage));
                return false;
            }
        }

        public static ScreenProviderConfig TryLoad(IMyEntity storageEntity)
        {
            if (storageEntity.Storage == null)
                return null;

            string value;
            if (storageEntity.Storage.TryGetValue(Constants.StorageGuid, out value) && !string.IsNullOrEmpty(value))
            {
                try
                {
                    var current = Deserialize<ScreenProviderConfig>(value);
                    if (current == null)
                        return null;

                    var supported = current.NormalizeComponentSchema();
#if DEBUG
                    MyLog.Default.WriteLine(supported ? $"[LcdMod] Loaded component config schema {current.SchemaVersion}." : 
#else
                    if(!supported) MyLog.Default.WriteLine(
#endif
                    $"[LcdMod] Loaded newer component config schema {current.SchemaVersion} read-only; current schema is {ScreenProviderConfig.COMPONENT_SCHEMA_VERSION}.");
                    return current;
                }
                catch (Exception exception)
                {
                    // The component key is authoritative once present. Never restore the original
                    // legacy snapshot and erase edits made after migration.
                    ErrorHandlerHelper.LogError(exception, typeof(ScreenProviderConfigStorage));
                    return null;
                }
            }

            if (!storageEntity.Storage.TryGetValue(Constants.V0StorageGuid, out value) || string.IsNullOrEmpty(value))
                return null;

            try
            {
                var legacy = Deserialize<LegacyScreenProviderConfig>(value);
                if (legacy == null)
                    return null;

                var migrated = LegacyV0Migrator.Migrate(legacy);

                // Verify the complete new graph can round-trip before writing the migration marker.
                var verificationBytes = MyAPIGateway.Utilities.SerializeToBinary(migrated);
                var verified = MyAPIGateway.Utilities.SerializeFromBinary<ScreenProviderConfig>(verificationBytes);
                if (verified == null)
                    throw new Exception("Component config migration verification failed");

                if (!verified.NormalizeComponentSchema())
                    throw new Exception("Migrated component schema is unsupported by this build");
                if (!Save(storageEntity, verified))
                    throw new Exception("Failed to persist migrated component config");

                MyLog.Default.WriteLine(
                    $"[LcdMod] Migrated legacy config to component schema {ScreenProviderConfig.COMPONENT_SCHEMA_VERSION}.");
                return verified;
            }
            catch (Exception exception)
            {
                // Save() is never reached on failure, leaving the legacy value untouched and the
                // component GUID absent so a corrected migrator can be tried in a later build.
                ErrorHandlerHelper.LogError(exception, typeof(ScreenProviderConfigStorage));
                return null;
            }
        }

        static T Deserialize<T>(string base64)
        {
            var data = Convert.FromBase64String(base64);
            return MyAPIGateway.Utilities.SerializeFromBinary<T>(data);
        }
    }
}
