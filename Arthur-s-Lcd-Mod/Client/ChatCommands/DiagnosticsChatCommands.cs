#if DEBUG || EXPERIMENTAL
using System.Linq;
using Generated;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.ChatCommands
{
    internal static class DiagnosticsChatCommands
    {
#if DEBUG
        /// <summary>
        /// Runs a deterministic 15-second look-at, scroll, and carousel workload on every local interactive LCD.
        /// One screen per frame is looked at unless <paramref name="allScreens"/> is true. Running it again cancels
        /// the benchmark.
        /// </summary>
        [ChatCommand("BenchmarkScreens")]
        public static void BenchmarkScreens(bool? allScreens)
        {
            Debugging.ScreenBenchmark.Start(allScreens ?? false);
        }

        /// <summary>
        /// Opens a diagnostic text input dialog.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_TextInput_Summary</loc>
        [ChatCommand("TextInput")]
        public static void TextInput(string[] strings)
        {
            TextInputHelper.SpawnForLocalPlayer(
                strings.FirstOrDefault(),
                s => MyAPIGateway.Utilities.ShowNotification("User typed: " + s),
                "Hello World!",
                strings.Length > 1 ? strings[1] : string.Empty);
        }

        /// <summary>
        /// Opens or closes the retained ADK visual-tree debugger for the last targeted surface.
        /// </summary>
        [ChatCommand("VisibleTreeDebug")]
        public static void VisibleTreeDebug(bool? enabled)
        {
            var service = LcdModSessionComponent.Client?.VisibleTreeDebug;
            LogHelper.LogInfo("[VisibleTreeDebug] command: service " + (service == null ? "missing" : "present"));
            if (service == null)
                return;
            bool open = enabled ?? !service.IsOpen;
            if (open)
                service.Open();
            else
                service.Close();
        }

        /// <summary>
        /// Counts UI property and pseudo-class changes into the frame profile log. Allocates per change, so it skews timings.
        /// </summary>
        [ChatCommand("TraceUiChanges")]
        public static void TraceUiChanges(bool? enabled)
        {
            bool enable = enabled ?? Acrux.Core.Control.ChangeTrace == null;
            Acrux.Core.Control.ChangeTrace = enable ? Debugging.FrameProfiler.TraceChange : (System.Action<Acrux.Core.Control, string>)null;
            MyAPIGateway.Utilities.ShowNotification("UI change tracing " + (enable ? "on" : "off"));
        }

        /// <summary>
        /// Redraws every reused retained LCD frame and reports elements that changed without invalidating.
        /// </summary>
        [ChatCommand("VerifyFrameCache")]
        public static void VerifyFrameCache(bool? enabled)
        {
            bool enable = enabled ?? Acrux.Lcd.LcdGuiHost.FrameCacheMismatch == null;
            Acrux.Lcd.LcdGuiHost.FrameCacheMismatch = enable ? ReportStaleFrame : (System.Action<string>)null;
            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                enable
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_VerifyFrameCache_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_VerifyFrameCache_Disabled_Message"));
        }

        /// <summary>
        /// Toggles the parsed style cache. While disabled, every new view reads its styles from disk again.
        /// </summary>
        [ChatCommand("StyleCache")]
        public static void StyleCache(bool? enabled)
        {
            bool enable = enabled ?? !Acrux.Styling.StyleSheet.CacheSources;
            Acrux.Styling.StyleSheet.CacheSources = enable;
            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                enable
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_StyleCache_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_StyleCache_Disabled_Message"));
        }

        /// <summary>
        /// Toggles view hot reload. While enabled, edited view and style XAML in the mod folder is rebuilt in place.
        /// </summary>
        [ChatCommand("HotReload")]
        public static void HotReloadViews(bool? enabled)
        {
            // Views are built by Acrux UI now; view hot reload is not bridged to client mods yet.
            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocHelper.GetLoc("LcdMod_ChatCommand_HotReload_Disabled_Message"));
        }

        /// <summary>
        /// Lists the missiles within <paramref name="radius"/> metres of the camera (default 5 km): which index found each
        /// (the game's missile index, the general entity index, or both), its ammo, distance and launcher.
        /// </summary>
        [ChatCommand("Missiles")]
        public static void Missiles(float? radius)
        {
            var camera = MyAPIGateway.Session.Camera;
            if (camera == null)
                return;
            var sphere = new VRageMath.BoundingSphereD(camera.Position, radius ?? 5000f);
            var indexed = new System.Collections.Generic.List<VRage.Game.Entity.MyEntity>();
            if (MyAPIGateway.Missiles != null)
                MyAPIGateway.Missiles.GetAllMissilesInSphere(ref sphere, indexed);
            var inIndex = new System.Collections.Generic.HashSet<long>();
            var all = new System.Collections.Generic.Dictionary<long, IMyMissile>();
            foreach (var entity in indexed)
            {
                var missile = entity as IMyMissile;
                if (missile == null)
                    continue;
                inIndex.Add(missile.EntityId);
                all[missile.EntityId] = missile;
            }
            var inEntities = new System.Collections.Generic.HashSet<long>();
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
            foreach (var entity in entities)
            {
                var missile = entity as IMyMissile;
                if (missile == null)
                    continue;
                inEntities.Add(missile.EntityId);
                all[missile.EntityId] = missile;
            }
            var text = new System.Text.StringBuilder();
            text.Append("Missile index: ").Append(inIndex.Count).Append("  entity index: ").Append(inEntities.Count)
                .Append("  (").Append(indexed.Count).Append(" from the missile index in all)\n\n");
            foreach (var pair in all)
            {
                IMyMissile missile = pair.Value;
                var launcher = MyAPIGateway.Entities.GetEntityById(missile.LauncherId) as VRage.Game.ModAPI.IMyCubeBlock;
                text.Append(missile.AmmoDefinition == null ? "?" : missile.AmmoDefinition.Id.SubtypeName)
                    .Append(inIndex.Contains(pair.Key) ? " [missile index]" : string.Empty)
                    .Append(inEntities.Contains(pair.Key) ? " [entity index]" : string.Empty)
                    .Append("  ").Append((int)VRageMath.Vector3D.Distance(missile.GetPosition(), camera.Position)).Append(" m")
                    .Append("  ").Append((int)missile.LinearVelocity.Length()).Append(" m/s")
                    .Append("  launcher ").Append(launcher == null ? "not found (" + missile.LauncherId + ")" : launcher.DisplayNameText)
                    .Append('\n');
            }
            LogHelper.LogInfo("[Missiles] " + text);
            MyAPIGateway.Utilities.ShowMissionScreen("Missiles", null, null, text.ToString());
        }

        static void ReportStaleFrame(string message)
        {
            LogHelper.Log(VRage.Utils.MyLogSeverity.Warning, message);
            MyAPIGateway.Utilities.ShowNotification(message, 2000, "Red");
        }
#endif

    }
}
#endif
