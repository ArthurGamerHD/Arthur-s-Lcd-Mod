// ReSharper disable RedundantUsingDirective
// ReSharper disable NotAccessedOutParameterVariable
using System;
using System.IO;
using LcdMod.Client.Audio.Xwma.Decoder;
using Sandbox.ModAPI;
using VRage.Game;

namespace LcdMod.Client.Audio
{
    internal enum GameAudioContainerKind
    {
        Unknown,
        PcmWave,
        Xwma
    }

    internal static class GameAudioPcmLoader
    {
        sealed class ResolvedAudioFile
        {
            public string Path;
            public MyObjectBuilder_Checkpoint.ModItem Mod;
            public bool IsMod;
            public bool UsedWavFallback;
        }
        public static bool IsSupportedAudioPath(string path)
        {
            if (GetContainerKind(path) != GameAudioContainerKind.Unknown)
                return true;

            string fallbackPath = GetWavFallbackPath(path);
            if (string.IsNullOrEmpty(fallbackPath))
                return false;

            try
            {
                string b;
                ResolvedAudioFile a;
                return TryResolveTrustedAudioPath(
                    fallbackPath,
                    out a,
                    out b);
            }
            catch
            {
                return false;
            }
        }

        public static GameAudioContainerKind GetContainerKind(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
                return GameAudioContainerKind.PcmWave;
            if (string.Equals(extension, ".xwm", StringComparison.OrdinalIgnoreCase))
                return GameAudioContainerKind.Xwma;
            return GameAudioContainerKind.Unknown;
        }

        public static string GetContainerDisplayName(GameAudioContainerKind containerKind)
        {
            switch (containerKind)
            {
                case GameAudioContainerKind.PcmWave:
                    return "PCM WAV";
                case GameAudioContainerKind.Xwma:
                    return "xWMA";
                default:
                    return "unknown";
            }
        }

        public static bool TryReadInGameContent(
            string wavePath,
            out PcmWaveData pcm,
            out string failureReason,
            out GameAudioContainerKind containerKind)
        {
            string a;
            bool b;
            return TryReadInGameContent(
                wavePath,
                out pcm,
                out failureReason,
                out containerKind,
                out a,
                out b);
        }

        public static bool TryReadInGameContent(
            string wavePath,
            out PcmWaveData pcm,
            out string failureReason,
            out GameAudioContainerKind containerKind,
            out string resolvedWavePath,
            out bool usedWavFallback)
        {
            pcm = null;
            failureReason = string.Empty;
            containerKind = GetContainerKind(wavePath);
            resolvedWavePath = null;
            usedWavFallback = false;

            try
            {
                ResolvedAudioFile resolved;
                if (!TryResolveTrustedAudioPath(
                        wavePath,
                        out resolved,
                        out failureReason))
                {
                    return false;
                }

                resolvedWavePath = resolved.Path;
                usedWavFallback = resolved.UsedWavFallback;
                containerKind = GetContainerKind(resolvedWavePath);

                using (BinaryReader reader = OpenResolvedAudio(resolved))
                {
                    return TryRead(
                        reader,
                        resolvedWavePath,
                        true,
                        out pcm,
                        out failureReason,
                        out containerKind);
                }
            }
            catch (Exception error)
            {
                pcm = null;
                failureReason = error.Message;
                return false;
            }
        }

        public static bool TryResolveInGameContentPath(
            string wavePath,
            out string resolvedWavePath,
            out bool usedWavFallback,
            out string failureReason)
        {
            resolvedWavePath = null;
            usedWavFallback = false;
            failureReason = string.Empty;

            string requestedPath = ToAudioGameContentPath(wavePath);
            if (string.IsNullOrEmpty(requestedPath))
            {
                failureReason = "Missing game audio path.";
                return false;
            }

            try
            {
                ResolvedAudioFile resolved;
                if (!TryResolveTrustedAudioPath(
                        requestedPath,
                        out resolved,
                        out failureReason))
                {
                    return false;
                }

                resolvedWavePath = resolved.Path;
                usedWavFallback = resolved.UsedWavFallback;
                return true;
            }
            catch (Exception error)
            {
                failureReason = error.Message;
                return false;
            }
        }

        public static string ToAudioGameContentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = NormalizePathSeparators(path.Trim());
            while (path.StartsWith("/", StringComparison.Ordinal))
                path = path.Substring(1);

            if (path.StartsWith("Audio/", StringComparison.OrdinalIgnoreCase))
                return path;

            return "Audio/" + path;
        }

        public static string ToDefinitionAudioPath(string gameContentPath)
        {
            if (string.IsNullOrWhiteSpace(gameContentPath))
                return gameContentPath;

            gameContentPath = NormalizePathSeparators(gameContentPath.Trim());
            if (gameContentPath.StartsWith("Audio/", StringComparison.OrdinalIgnoreCase))
                return gameContentPath.Substring("Audio/".Length);

            return gameContentPath;
        }

        public static bool TryRead(
            BinaryReader reader,
            string wavePath,
            out PcmWaveData pcm,
            out string failureReason,
            out GameAudioContainerKind containerKind)
        {
            return TryRead(
                reader,
                wavePath,
                false,
                out pcm,
                out failureReason,
                out containerKind);
        }

        public static bool TryRead(
            BinaryReader reader,
            string wavePath,
            bool allowTrustedLocalPayload,
            out PcmWaveData pcm,
            out string failureReason,
            out GameAudioContainerKind containerKind)
        {
            pcm = null;
            failureReason = string.Empty;
            containerKind = GameAudioContainerKind.Unknown;

            if (reader == null)
            {
                failureReason = "Missing input stream.";
                return false;
            }

            var extensionKind = GetContainerKind(wavePath);
            if (extensionKind == GameAudioContainerKind.Unknown)
            {
                failureReason = "Unsupported game audio extension: " + Path.GetExtension(wavePath);
                return false;
            }

            if (!TrySniffContainerKind(reader.BaseStream, out containerKind, out failureReason))
                return false;

            if (containerKind == GameAudioContainerKind.Unknown)
            {
                failureReason = "Unsupported RIFF audio form in " + Path.GetFileName(wavePath) + ".";
                return false;
            }

            switch (containerKind)
            {
                case GameAudioContainerKind.PcmWave:
                    // WAV assets already carry PCM samples in their RIFF data chunk.
                    // Do not run them through the xWMA codec path, even when the
                    // Space Engineers asset uses a .xwm extension for a WAVE RIFF.
                    return PcmWaveReader.TryRead(
                        reader,
                        allowTrustedLocalPayload,
                        out pcm,
                        out failureReason);

                case GameAudioContainerKind.Xwma:
                    reader.BaseStream.Position = 0;
                    return XwmaPcmDecoder.TryDecode(
                        reader.BaseStream,
                        out pcm,
                        out failureReason);

                default:
                    failureReason = "Unsupported game audio container.";
                    return false;
            }
        }

        static bool TryResolveTrustedAudioPath(
            string wavePath,
            out ResolvedAudioFile resolved,
            out string failureReason)
        {
            resolved = null;
            failureReason = string.Empty;

            string requestedPath = ToAudioGameContentPath(wavePath);
            if (string.IsNullOrEmpty(requestedPath))
            {
                failureReason = "Missing game audio path.";
                return false;
            }

            if (TryResolveExactTrustedAudioPath(
                    requestedPath,
                    false,
                    out resolved))
            {
                return true;
            }

            string fallbackPath = GetWavFallbackPath(requestedPath);
            if (!string.IsNullOrEmpty(fallbackPath) &&
                !string.Equals(fallbackPath, requestedPath, StringComparison.OrdinalIgnoreCase) &&
                TryResolveExactTrustedAudioPath(
                    fallbackPath,
                    true,
                    out resolved))
            {
                return true;
            }

            failureReason = "Unable to find audio file: '" + ToReportPath(requestedPath) + "'";
            if (!string.IsNullOrEmpty(fallbackPath) &&
                !string.Equals(fallbackPath, requestedPath, StringComparison.OrdinalIgnoreCase))
            {
                failureReason += " (also tried '" + ToReportPath(fallbackPath) + "')";
            }

            return false;
        }

        static bool TryResolveExactTrustedAudioPath(
            string path,
            bool usedWavFallback,
            out ResolvedAudioFile resolved)
        {
            resolved = null;

            if (string.IsNullOrWhiteSpace(path) || MyAPIGateway.Utilities == null)
                return false;

            var session = MyAPIGateway.Session;
            if (session != null && session.Mods != null)
            {
                for (int i = 0; i < session.Mods.Count; i++)
                {
                    var mod = session.Mods[i];
                    try
                    {
                        if (MyAPIGateway.Utilities.FileExistsInModLocation(path, mod))
                        {
                            resolved = new ResolvedAudioFile
                            {
                                Path = path,
                                Mod = mod,
                                IsMod = true,
                                UsedWavFallback = usedWavFallback
                            };
                            return true;
                        }
                    }
                    catch
                    {
                        // Bad mod locations should not prevent game-content fallback.
                    }
                }
            }

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInGameContent(path))
                {
                    resolved = new ResolvedAudioFile
                    {
                        Path = path,
                        IsMod = false,
                        UsedWavFallback = usedWavFallback
                    };
                    return true;
                }
            }
            catch
            {
                // file not found, ignoring
            }

            return false;
        }

        static BinaryReader OpenResolvedAudio(ResolvedAudioFile resolved)
        {
            if (resolved == null)
                throw new FileNotFoundException("Missing resolved audio path.");

            if (resolved.IsMod)
                return MyAPIGateway.Utilities.ReadBinaryFileInModLocation(
                    resolved.Path,
                    resolved.Mod);

            return MyAPIGateway.Utilities.ReadBinaryFileInGameContent(resolved.Path);
        }

        static string GetWavFallbackPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = NormalizePathSeparators(path.Trim());
            int slashIndex = path.LastIndexOf('/');
            string directory = slashIndex >= 0 ? path.Substring(0, slashIndex + 1) : string.Empty;
            string fileName = slashIndex >= 0 ? path.Substring(slashIndex + 1) : path;
            if (string.IsNullOrEmpty(fileName))
                return null;

            int dotIndex = fileName.LastIndexOf('.');
            string fileNameWithoutExtension = dotIndex > 0 ? fileName.Substring(0, dotIndex) : fileName;
            if (string.IsNullOrEmpty(fileNameWithoutExtension))
                return null;

            return directory + fileNameWithoutExtension + ".wav";
        }

        static bool TrySniffContainerKind(
            Stream stream,
            out GameAudioContainerKind containerKind,
            out string failureReason)
        {
            containerKind = GameAudioContainerKind.Unknown;
            failureReason = string.Empty;

            if (stream == null)
            {
                failureReason = "Missing input stream.";
                return false;
            }

            if (!stream.CanRead || !stream.CanSeek)
            {
                failureReason = "Audio stream must be readable and seekable.";
                return false;
            }

            try
            {
                stream.Position = 0;

                if (stream.Length < 12)
                {
                    failureReason = "File is too small to be a RIFF audio file.";
                    return false;
                }

                var riff = ReadFourCc(stream);
                stream.Position += 4;
                var form = ReadFourCc(stream);
                stream.Position = 0;

                if (riff != "RIFF")
                {
                    failureReason = "Expected RIFF audio container.";
                    return false;
                }

                if (form == "WAVE")
                    containerKind = GameAudioContainerKind.PcmWave;
                else if (form == "XWMA")
                    containerKind = GameAudioContainerKind.Xwma;

                return true;
            }
            catch (Exception error)
            {
                failureReason = error.Message;
                return false;
            }
        }

        static string ReadFourCc(Stream stream)
        {
            var buffer = new byte[4];
            int read = stream.Read(buffer, 0, buffer.Length);
            return read == buffer.Length
                ? System.Text.Encoding.ASCII.GetString(buffer)
                : string.Empty;
        }

        static string NormalizePathSeparators(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        static string ToReportPath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : NormalizePathSeparators(path).Replace('/', '\\');
        }
    }
}
