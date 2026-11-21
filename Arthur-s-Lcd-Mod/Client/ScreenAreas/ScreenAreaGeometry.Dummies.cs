using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.ScreenAreas
{
    public static partial class ScreenAreaGeometry
    {
        static readonly Dictionary<string, Vector3?> ConnectorPointCache =
            new Dictionary<string, Vector3?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The point where a connector connects, in block-local coordinates, read from its model file the way
        /// MyShipConnector.LoadDummies finds it. False for ejectors and blocks without a connector dummy.
        /// </summary>
        public static bool TryGetConnectorLocalPoint(IMyCubeBlock block, out Vector3 localPoint)
        {
            localPoint = Vector3.Zero;
            var definition = block == null
                ? null
                : MyDefinitionManager.Static.GetCubeBlockDefinition(block.BlockDefinition);
            // The game reads the definition's model, not the block's current (build stage) model.
            var assetName = definition?.Model;
            if (string.IsNullOrWhiteSpace(assetName))
                return false;

            Vector3? cached;
            if (!ConnectorPointCache.TryGetValue(assetName, out cached))
            {
                cached = ReadConnectorPoint(assetName);
                ConnectorPointCache[assetName] = cached;
            }

            if (!cached.HasValue)
                return false;

            localPoint = cached.Value;
            return true;
        }

        static Vector3? ReadConnectorPoint(string assetName)
        {
            var modelPath = ToModelPath(assetName);
            if (string.IsNullOrWhiteSpace(modelPath))
                return null;

            try
            {
                using (var reader = OpenMwm(modelPath))
                {
                    List<KeyValuePair<string, Matrix>> dummies;
                    if (reader == null || !MinimalMwmReader.TryReadDummies(reader, out dummies))
                    {
                        LogHelper.LogOnce("dummies:none:" + modelPath, "no dummies read from " + modelPath);
                        return null;
                    }

                    // Same rule as MyShipConnector.LoadDummies: the first "connector" or "ejector" dummy decides.
                    for (int i = 0; i < dummies.Count; i++)
                    {
                        var name = dummies[i].Key.ToLowerInvariant();
                        var isConnector = name.Contains("connector");
                        if (!isConnector && !name.Contains("ejector"))
                            continue;
                        if (!isConnector)
                            return null;

                        return Matrix.Normalize(dummies[i].Value).Translation;
                    }

                    LogHelper.LogOnce("dummies:no-connector:" + modelPath, "no connector dummy in " + modelPath);
                    return null;
                }
            }
            catch (Exception e)
            {
                LogHelper.LogOnce("dummies:error:" + modelPath, "reading dummies of " + modelPath + " failed: " + e);
                return null;
            }
        }
    }
}
