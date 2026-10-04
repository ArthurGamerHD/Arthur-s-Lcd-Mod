using System;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Client.Apps.MediaPlayer;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.Apps
{
    public sealed partial class MediaPlayerApp : IVisualizerSettingsHost
    {
        VisualizerSettingsViewModel _visualizerSettings;

        internal string ConfigureVisualizerText => LocHelper.GetLoc(Constants.MOD_PREFIX + "MediaPlayer_ConfigureVisualizer");

        static void EnsureVisualizerDefaults()
        {
            if (MediaVisualizerDefaults.Loader == null)
                MediaVisualizerDefaults.Loader = LoadDefaultVisualizerSettings;
        }

        static MediaVisualizerInstanceSettings[] LoadDefaultVisualizerSettings()
        {
            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadFileInModLocation(
                           MediaVisualizerDefaults.FILE, LcdModSessionComponent.CurrentModItem))
                {
                    var document = MyAPIGateway.Utilities.SerializeFromXML<MediaVisualizerSettingsDocument>(reader.ReadToEnd());
                    return document == null ? null : document.Instances;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(MediaVisualizerDefaults));
                return null;
            }
        }

        internal void OpenVisualizerSettings()
        {
            EnsureVisualizerDefaults();
            if (_visualizerSettings != null)
                return;
            var model = new VisualizerSettingsViewModel(this);
            var view = new VisualizerSettingsView(model);
            view.Closed += () => _visualizerSettings = null;
            if (ShowDialog(view))
                _visualizerSettings = model;
        }

        MediaVisualizerInstanceSettings[] IVisualizerSettingsHost.VisualizerInstances => VisualizerInstances;

        void IVisualizerSettingsHost.SetVisualizerInstances(MediaVisualizerInstanceSettings[] instances, bool persist)
        {
            SetVisualizerInstances(instances, persist);
        }

        bool IVisualizerSettingsHost.HasUserVisualizerSettings
        {
            get
            {
                var saved = LocalConfigManager.EnsureConfig().MediaVisualizerInstances;
                return saved != null;
            }
        }

        void IVisualizerSettingsHost.SaveUserVisualizerSettings()
        {
            LocalConfigManager.EnsureConfig().MediaVisualizerInstances =
                MediaVisualizerSettingsResolver.Expand(VisualizerInstances);
            LocalConfigManager.Save();
        }

        void IVisualizerSettingsHost.LoadUserVisualizerSettings()
        {
            var saved = LocalConfigManager.EnsureConfig().MediaVisualizerInstances;
            if (saved != null)
                SetVisualizerInstances(MediaVisualizerInstanceSettings.Copy(saved), true);
        }

        /// <summary>Opens the settings as XML in the cockpit text box; the edited text is imported when it closes.</summary>
        void IVisualizerSettingsHost.EditVisualizerXml()
        {
            var document = new MediaVisualizerSettingsDocument
            {
                Instances = MediaVisualizerSettingsResolver.Expand(VisualizerInstances)
            };
            string xml = MyAPIGateway.Utilities.SerializeToXML(document);
            TextInputHelper.SpawnForLocalPlayer(
                LocHelper.GetLoc(Constants.MOD_PREFIX + "MediaPlayer_VisualizerXmlTitle"),
                edited => ImportVisualizerXml(xml, edited),
                xml,
                Title);
        }

        void ImportVisualizerXml(string original, string edited)
        {
            if (string.IsNullOrWhiteSpace(edited) || string.Equals(original, edited, StringComparison.Ordinal))
                return;
            MediaVisualizerSettingsDocument document;
            try
            {
                document = MyAPIGateway.Utilities.SerializeFromXML<MediaVisualizerSettingsDocument>(edited);
            }
            catch (Exception e)
            {
                ShowMessage(Title,
                    string.Format(LocHelper.GetLoc(Constants.MOD_PREFIX + "MediaPlayer_VisualizerXmlInvalid"), e.Message),
                    new DialogButton(LocHelper.GetLoc(Constants.MOD_PREFIX + "Common_Button_OK"), null, true));
                return;
            }
            if (document == null)
                return;
            SetVisualizerInstances(MediaVisualizerSettingsResolver.Expand(document.Instances), true);
            if (_visualizerSettings != null)
                _visualizerSettings.Reload();
        }
    }
}
