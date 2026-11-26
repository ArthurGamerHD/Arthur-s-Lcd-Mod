using Generated;
using LcdMod.Client.Helpers;

namespace LcdMod.Client.Apps.MultiFunction
{
    /// <summary>An app the multi-function display can open, as its [MultiFunction] attribute describes it.</summary>
    public sealed class MultiFunctionAppInfo
    {
        public MultiFunctionAppInfo(AppType appType, string nameKey, string icon, string scriptId)
        {
            ScriptId = scriptId ?? string.Empty;
            AppType = appType;
            NameKey = nameKey;
            Icon = string.IsNullOrWhiteSpace(icon) ? "MissingIcon" : icon;
        }

        public AppType AppType { get; }
        public string NameKey { get; }
        public string Icon { get; }

        /// <summary>The script id of the app's own surface, which decides the terminal controls that apply to it.</summary>
        public string ScriptId { get; }
        public string Name => LocHelper.GetLoc(NameKey);
    }
}
