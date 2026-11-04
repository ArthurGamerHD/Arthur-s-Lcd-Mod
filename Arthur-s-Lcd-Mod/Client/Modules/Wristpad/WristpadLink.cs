using System;
using LcdMod.Client.Gui;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Wristpad
{
    /// <summary>
    /// Mod-message link to the Wristpad Framework. The framework locks the camera on the wrist panel while the pad is
    /// raised, so eye tracking cannot aim; while it is up and runs one of our screens, <see cref="WristpadInputScreen"/>
    /// drives the panel from the mouse instead. Every message is a <c>MyTuple&lt;string, long&gt;</c>: (name, panel EntityId).
    /// Without the framework nothing ever arrives and this stays idle.
    /// </summary>
    internal static class WristpadLink
    {
        // Must match WristpadFramework.WristpadMessages.Channel.
        const long CHANNEL = 3946107714L;

        // Wristpad -> mods
        const string OPENED = "Opened";
        const string CLOSED = "Closed";
        // mods -> Wristpad
        const string QUERY = "Query";
        const string CLOSE = "Close";
        const string CAPTURE = "Capture";
        const string RELEASE = "Release";

        static bool _registered;

        // EntityId of the local player's raised wrist panel, or 0 while the pad is down.
        public static long OpenPanelId { get; private set; }

        public static void Load()
        {
            if (_registered || MyAPIGateway.Utilities.IsDedicated)
                return;
            MyAPIGateway.Utilities.RegisterMessageHandler(CHANNEL, Handle);
            _registered = true;
            // Covers load order and a pad that was already up.
            Send(QUERY, 0L);
        }

        public static void Unload()
        {
            WristpadInputScreen.CloseCurrent();
            if (_registered)
                MyAPIGateway.Utilities.UnregisterMessageHandler(CHANNEL, Handle);
            _registered = false;
            OpenPanelId = 0L;
        }

        // Opens the input screen while the pad is up, e.g. again after the panel switched to one of our scripts.
        public static void Update()
        {
            if (OpenPanelId == 0L)
                return;

            try
            {
                var panel = GetOpenPanel();
                if (panel == null)
                {
                    OpenPanelId = 0L;
                    WristpadInputScreen.CloseCurrent();
                    return;
                }

                WristpadInputScreen.TryOpenFor(panel);
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(WristpadLink));
            }
        }

        // The raised wrist panel, or null.
        public static IMyTerminalBlock GetOpenPanel()
        {
            if (OpenPanelId == 0L)
                return null;
            var panel = MyAPIGateway.Entities.GetEntityById(OpenPanelId) as IMyTerminalBlock;
            return panel == null || panel.MarkedForClose || panel.Closed ? null : panel;
        }

        public static void RequestClose() => Send(CLOSE, OpenPanelId);

        // Our screen took the pad's input: the framework ignores its Reload toggle until ReleaseInput.
        public static void CaptureInput(long panelId) => Send(CAPTURE, panelId);

        public static void ReleaseInput(long panelId) => Send(RELEASE, panelId);

        static void Send(string name, long panelId)
        {
            if (!_registered)
                return;
            try
            {
                MyAPIGateway.Utilities.SendModMessage(CHANNEL, new MyTuple<string, long>(name, panelId));
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(WristpadLink));
            }
        }

        static void Handle(object message)
        {
            try
            {
                // Our own requests come back on this channel too; they match no case below.
                if (!(message is MyTuple<string, long>))
                    return;
                var tuple = (MyTuple<string, long>)message;
                switch (tuple.Item1)
                {
                    case OPENED:
                        OpenPanelId = tuple.Item2;
                        Update();
                        break;
                    case CLOSED:
                        if (OpenPanelId == tuple.Item2 || tuple.Item2 == 0L)
                        {
                            OpenPanelId = 0L;
                            WristpadInputScreen.CloseCurrent();
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                ErrorHandlerHelper.LogError(exception, typeof(WristpadLink));
            }
        }
    }
}
