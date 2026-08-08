using Generated;

namespace LcdMod.Client.ChatCommands
{
    internal static class AudioChatCommands
    {
        /// <summary>
        /// Imports a local WAV file into the audio library.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_ImportLocalAudio_Summary</loc>
        [ChatCommand("ImportLocalAudio")]
        public static void ImportLocalAudio(string sourcePath)
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.AudioImport == null)
                return;

            client.AudioImport.ImportLocalAudio(sourcePath);
        }

        /// <summary>
        /// Imports the WAV files listed in audio_import.txt.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_ImportAudios_Summary</loc>
        [ChatCommand("ImportAudios")]
        public static void ImportAudios()
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.AudioImport == null)
                return;

            client.AudioImport.ImportAudios();
        }

        /// <summary>
        /// Plays a local WAV file for the current player.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_PlayAudio_Summary</loc>
        [ChatCommand("PlayAudio")]
        public static void PlayAudio(string fileName)
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.AudioPoc == null)
                return;

            client.AudioPoc.PlayAudio(fileName);
        }

        /// <summary>
        /// Plays a game audio definition by subtype.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_PlayGameAudio_Summary</loc>
        [ChatCommand("PlayGameAudio")]
        public static void PlayGameAudio(string subtype)
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.AudioPoc == null)
                return;

            client.AudioPoc.PlayGameAudio(subtype);
        }

        /// <summary>
        /// Broadcasts an imported audio asset to LCD media players.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_StreamAudio_Summary</loc>
        [ChatCommand("StreamAudio")]
        public static void StreamAudio(string query)
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.AudioBroadcast == null)
                return;

            client.AudioBroadcast.StreamAudio(query);
        }

        /// <summary>
        /// Tests referenced game audio files and writes a compatibility report.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_TestAudio_Summary</loc>
        [ChatCommand("TestAudio")]
        public static void TestAllGameAudio()
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.AudioTestReport == null)
                return;

            client.AudioTestReport.TestAllGameAudio();
        }
    }
}
