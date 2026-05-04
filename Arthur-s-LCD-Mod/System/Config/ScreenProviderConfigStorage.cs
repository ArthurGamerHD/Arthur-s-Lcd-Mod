using System;
using Graph.Helpers;
using Graph.System.Config.Models;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Graph.System.Config
{
    public static class ScreenProviderConfigStorage
    {
        public static void Save(IMyEntity storageEntity, ScreenProviderConfig providerConfig)
        {
            try
            {
                if (storageEntity.Storage == null)
                    storageEntity.Storage = new MyModStorageComponent();

                var base64 = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(providerConfig));

                if (string.IsNullOrEmpty(base64))
                    throw new Exception("Invalid storage config");

                storageEntity.Storage[Constants.StorageGuid] = base64;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(ScreenProviderConfigStorage));
            }
        }

        public static ScreenProviderConfig TryLoad(IMyEntity storageEntity)
        {
            if (storageEntity.Storage == null)
                return null;

            string value;
            if (storageEntity.Storage.TryGetValue(Constants.StorageGuid, out value) && !string.IsNullOrEmpty(value))
            {
                var data = Convert.FromBase64String(value);
                return MyAPIGateway.Utilities.SerializeFromBinary<ScreenProviderConfig>(data);
            }

            return null;
        }
    }
}
