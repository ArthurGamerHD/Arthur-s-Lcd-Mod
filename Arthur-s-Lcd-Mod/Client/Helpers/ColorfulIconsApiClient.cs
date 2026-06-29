using System;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;
using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;

namespace LcdMod.Client.Helpers
{
    /// <summary>
    /// Client wrapper for the Colorful Icons inter-mod API.
    /// </summary>
    public sealed class ColorfulIconsApiClient
    {
        public const long WORKSHOP_ID = 801185519L;
        const long REGISTRATION_CHANNEL = (WORKSHOP_ID << 8) | 1;

        public static Version ClientApiVersion => new Version(0, 1, 0);
        public static string Scope => MyAPIGateway.Utilities.GamePaths.ModScopeName;

        public delegate void ConfigChangedHandler(ColorfulIconsConfig config);

        Func<string> _getConfigXml;
        Func<byte[]> _getConfigBinary;
        Action<Action> _subscribeConfigChanged;
        Action<Action> _unsubscribeConfigChanged;
        bool _listening;

        public ColorfulIconsApiClient(long replyChannel)
        {
            if (replyChannel == REGISTRATION_CHANNEL)
                throw new ArgumentException("The reply channel must differ from the registration channel.", "replyChannel");

            ReplyChannel = replyChannel;
        }

        public event Action<ColorfulIconsApiClient> Initialized;
        public event ConfigChangedHandler ConfigChanged;

        public ColorfulIconsConfig Config { get; private set; }
        public Version ServerApiVersion { get; private set; }
        public bool IsReady { get; private set; }
        public long ReplyChannel { get; private set; }

        public void Init()
        {
            RequestApi();
        }

        public void RequestApi()
        {
            Listen();

            try
            {
                MyAPIGateway.Utilities.SendModMessage(REGISTRATION_CHANNEL, ReplyChannel);
            }
            catch (Exception exception)
            {
                LogWarning("API request failed", exception);
            }
        }

        public void Listen()
        {
            if (_listening)
                return;

            MyAPIGateway.Utilities.RegisterMessageHandler(ReplyChannel, OnApiResponse);
            _listening = true;
        }

        public string GetConfigXml()
        {
            var getter = _getConfigXml;
            if (getter == null)
                return null;

            try
            {
                return getter();
            }
            catch (Exception exception)
            {
                LogWarning("GetConfig failed", exception);
                return null;
            }
        }

        public byte[] GetConfigBinary()
        {
            var getter = _getConfigBinary;
            if (getter == null)
                return null;

            try
            {
                return getter();
            }
            catch (Exception exception)
            {
                LogWarning("GetConfigBinary failed", exception);
                return null;
            }
        }

        public ColorfulIconsConfig GetConfig()
        {
            ColorfulIconsConfig config = null;
            var binary = GetConfigBinary();

            if (binary != null && binary.Length > 0)
            {
                try
                {
                    var wireConfig = MyAPIGateway.Utilities
                        .SerializeFromBinary<ColorfulIconsConfigProtoContract>(binary);

                    if (wireConfig != null)
                        config = wireConfig;
                }
                catch (Exception exception)
                {
                    LogWarning("Binary config deserialization failed", exception);
                }
            }

            if (config == null)
            {
                var xml = GetConfigXml();
                if (!string.IsNullOrWhiteSpace(xml))
                {
                    try
                    {
                        config = MyAPIGateway.Utilities.SerializeFromXML<ColorfulIconsConfig>(xml);
                    }
                    catch (Exception exception)
                    {
                        LogWarning("XML config deserialization failed", exception);
                    }
                }
            }

            if (config != null)
                Config = config;

            return config;
        }

        public void Close()
        {
            DetachRemoteConfigHandler();

            if (_listening)
            {
                try
                {
                    MyAPIGateway.Utilities.UnregisterMessageHandler(ReplyChannel, OnApiResponse);
                }
                catch (Exception exception)
                {
                    LogWarning("Reply listener removal failed", exception);
                }

                _listening = false;
            }

            ResetApiState();
            Initialized = null;
            ConfigChanged = null;
        }

        void OnApiResponse(object payload)
        {
            var apiData = payload as ApiData;
            if (apiData == null)
                return;

            try
            {
                Delegate tmp;

                var getConfigVersion = apiData.TryGetValue("GetConfigVersion", out tmp)
                    ? tmp as Func<Version>
                    : null;
                var getConfigXml = apiData.TryGetValue("GetConfig", out tmp)
                    ? tmp as Func<string>
                    : null;
                var getConfigBinary = apiData.TryGetValue("GetConfigBinary", out tmp)
                    ? tmp as Func<byte[]>
                    : null;
                var subscribeConfigChanged = apiData.TryGetValue("SubscribeConfigChanged", out tmp)
                    ? tmp as Action<Action>
                    : null;
                var unsubscribeConfigChanged = apiData.TryGetValue("UnsubscribeConfigChanged", out tmp)
                    ? tmp as Action<Action>
                    : null;

                if (getConfigVersion == null ||
                    getConfigXml == null ||
                    getConfigBinary == null ||
                    subscribeConfigChanged == null ||
                    unsubscribeConfigChanged == null)
                {
                    MyLog.Default.Log(MyLogSeverity.Warning,
                        "[" + Scope + "] Colorful Icons API response is missing required delegates.");
                    return;
                }

                var apiVersion = getConfigVersion();
                if (apiVersion == null || !ClientApiVersion.Equals(apiVersion))
                {
                    MyLog.Default.Log(MyLogSeverity.Warning,
                        "[" + Scope + "] Colorful Icons API version mismatch. Client " +
                        ClientApiVersion + ", server " + apiVersion + ".");
                    return;
                }

                DetachRemoteConfigHandler();

                _getConfigXml = getConfigXml;
                _getConfigBinary = getConfigBinary;
                _subscribeConfigChanged = subscribeConfigChanged;
                _unsubscribeConfigChanged = unsubscribeConfigChanged;
                ServerApiVersion = apiVersion;
                IsReady = false;

                try
                {
                    _subscribeConfigChanged(OnRemoteConfigChanged);
                }
                catch
                {
                    DetachRemoteConfigHandler();
                    ResetApiState();
                    throw;
                }

                IsReady = true;
                GetConfig();
                RaiseInitialized();
            }
            catch (Exception exception)
            {
                LogWarning("API response binding failed", exception);
            }
        }

        void OnRemoteConfigChanged()
        {
            var snapshot = GetConfig();
            var handlers = ConfigChanged;
            if (handlers == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (ConfigChangedHandler)@delegate;
                try
                {
                    handler(snapshot);
                }
                catch (Exception exception)
                {
                    LogWarning("A ConfigChanged subscriber threw an exception", exception);
                }
            }
        }

        void RaiseInitialized()
        {
            var handlers = Initialized;
            if (handlers == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (Action<ColorfulIconsApiClient>)@delegate;
                try
                {
                    handler(this);
                }
                catch (Exception exception)
                {
                    LogWarning("An Initialized subscriber threw an exception", exception);
                }
            }
        }

        void DetachRemoteConfigHandler()
        {
            var unsubscribe = _unsubscribeConfigChanged;
            if (unsubscribe == null)
                return;

            try
            {
                unsubscribe(OnRemoteConfigChanged);
            }
            catch (Exception exception)
            {
                LogWarning("Remote ConfigChanged unsubscription failed", exception);
            }
        }

        void ResetApiState()
        {
            _getConfigXml = null;
            _getConfigBinary = null;
            _subscribeConfigChanged = null;
            _unsubscribeConfigChanged = null;
            Config = null;
            ServerApiVersion = null;
            IsReady = false;
        }

        static void LogWarning(string message, Exception exception)
        {
            MyLog.Default.Log(MyLogSeverity.Warning,
                "[" + Scope + "] Colorful Icons API: " + message + ": " + exception);
        }
    }

    [ProtoContract]
    internal sealed class ColorfulIconsConfigProtoContract
    {
        [ProtoMember(1)] public bool? Ores { get; set; }
        [ProtoMember(2)] public bool? Ingots { get; set; }
        [ProtoMember(3)] public bool? Components { get; set; }
        [ProtoMember(4)] public bool? OldComponents { get; set; }
        [ProtoMember(5)] public bool? Blocks { get; set; }
        [ProtoMember(6)] public bool? Tools { get; set; }
        [ProtoMember(7)] public bool? FixToolColors { get; set; }
        [ProtoMember(8)] public bool? ForceOverride { get; set; }

        public static implicit operator ColorfulIconsConfig(ColorfulIconsConfigProtoContract contract)
        {
            return new ColorfulIconsConfig
            {
                Ores = contract.Ores ?? ColorfulIconsConfig.DEFAULT_ORES,
                Ingots = contract.Ingots ?? ColorfulIconsConfig.DEFAULT_INGOTS,
                Components = contract.Components ?? ColorfulIconsConfig.DEFAULT_COMPONENTS,
                OldComponents = contract.OldComponents ?? ColorfulIconsConfig.DEFAULT_OLD_COMPONENTS,
                Blocks = contract.Blocks ?? ColorfulIconsConfig.DEFAULT_BLOCKS,
                Tools = contract.Tools ?? ColorfulIconsConfig.DEFAULT_TOOLS,
                FixToolColors = contract.FixToolColors ?? ColorfulIconsConfig.DEFAULT_FIX_TOOL_COLORS,
                ForceOverride = contract.ForceOverride ?? ColorfulIconsConfig.DEFAULT_FORCE_OVERRIDE
            };
        }
    }

    [ProtoContract]
    [XmlRoot("ModSettings")]
    public class ColorfulIconsConfig
    {
        public const bool DEFAULT_BLOCKS = true;
        public const bool DEFAULT_COMPONENTS = true;
        public const bool DEFAULT_OLD_COMPONENTS = false;
        public const bool DEFAULT_INGOTS = true;
        public const bool DEFAULT_ORES = true;
        public const bool DEFAULT_TOOLS = true;
        public const bool DEFAULT_FIX_TOOL_COLORS = false;
        public const bool DEFAULT_FORCE_OVERRIDE = false;

        [XmlElement(Order = 1)]
        [DefaultValue(DEFAULT_ORES)]
        public bool Ores { get; set; }

        [XmlElement(Order = 2)]
        [DefaultValue(DEFAULT_INGOTS)]
        public bool Ingots { get; set; }

        [XmlElement(Order = 3)]
        [DefaultValue(DEFAULT_COMPONENTS)]
        public bool Components { get; set; }

        [XmlElement(Order = 4)]
        [DefaultValue(DEFAULT_OLD_COMPONENTS)]
        public bool OldComponents { get; set; }

        [XmlElement(Order = 5)]
        [DefaultValue(DEFAULT_BLOCKS)]
        public bool Blocks { get; set; }

        [XmlElement(Order = 6)]
        [DefaultValue(DEFAULT_TOOLS)]
        public bool Tools { get; set; }

        [XmlElement(Order = 7)]
        [DefaultValue(DEFAULT_FIX_TOOL_COLORS)]
        public bool FixToolColors { get; set; }

        [XmlElement(Order = 8)]
        [DefaultValue(DEFAULT_FORCE_OVERRIDE)]
        public bool ForceOverride { get; set; }

        public ColorfulIconsConfig()
        {
            Ores = DEFAULT_ORES;
            Ingots = DEFAULT_INGOTS;
            Components = DEFAULT_COMPONENTS;
            OldComponents = DEFAULT_OLD_COMPONENTS;
            Blocks = DEFAULT_BLOCKS;
            Tools = DEFAULT_TOOLS;
            FixToolColors = DEFAULT_FIX_TOOL_COLORS;
            ForceOverride = DEFAULT_FORCE_OVERRIDE;
        }
    }
}
