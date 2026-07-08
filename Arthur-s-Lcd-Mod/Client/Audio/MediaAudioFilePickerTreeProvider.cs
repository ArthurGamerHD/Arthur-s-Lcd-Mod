#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Common.Audio;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    sealed class MediaAudioFileReference
    {
        public const string SOURCE_LOCAL = "Local";
        public const string SOURCE_CONTENT = "Content";

        public string Source { get; set; }
        public string DefinitionPath { get; set; }
        public string GameContentPath { get; set; }
        public string FirstSoundSubtype { get; set; }
        public string FirstWaveSlot { get; set; }
        public int ReferenceCount { get; set; }
        public AudioAssetMetadata LocalAsset { get; set; }

        public bool IsLocal
        {
            get { return string.Equals(Source, SOURCE_LOCAL, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsContent
        {
            get { return string.Equals(Source, SOURCE_CONTENT, StringComparison.OrdinalIgnoreCase); }
        }
    }

    static class MediaAudioFilePickerTreeProvider
    {
        sealed class ContentAudioReference
        {
            public string Path;
            public string FirstSoundSubtype;
            public string FirstWaveSlot;
            public int ReferenceCount;
        }

        public static List<FolderModel> BuildRoots()
        {
            return new List<FolderModel>
            {
                BuildLocalRoot(),
                BuildContentRoot()
            };
        }

        static FolderModel BuildLocalRoot()
        {
            var root = new FolderModel
            {
                Name = MediaAudioFileReference.SOURCE_LOCAL,
                FullPath = MediaAudioFileReference.SOURCE_LOCAL,
                Subtitle = "Imported local audio"
            };

            var metadata = LoadAudioMetadata();
            if (metadata == null || metadata.Assets == null)
                return root;

            for (int i = 0; i < metadata.Assets.Count; i++)
            {
                var asset = metadata.Assets[i];
                if (asset == null || string.IsNullOrWhiteSpace(asset.RuntimePath) ||
                    !AudioLibraryStorage.RuntimeWaveExists(asset))
                    continue;

                var fileName = GetDisplayFileName(asset);
                root.Files.Add(new FileModel
                {
                    Name = fileName,
                    FullPath = MediaAudioFileReference.SOURCE_LOCAL + "/" + fileName,
                    Subtitle = BuildLocalSubtitle(asset),
                    Tag = new MediaAudioFileReference
                    {
                        Source = MediaAudioFileReference.SOURCE_LOCAL,
                        DefinitionPath = fileName,
                        GameContentPath = asset.RuntimePath,
                        LocalAsset = asset,
                        ReferenceCount = 1
                    }
                });
            }

            return root;
        }

        static FolderModel BuildContentRoot()
        {
            var root = new FolderModel
            {
                Name = MediaAudioFileReference.SOURCE_CONTENT,
                FullPath = MediaAudioFileReference.SOURCE_CONTENT,
                Subtitle = "Space Engineers audio definitions"
            };

            var references = CollectContentAudioReferences();
            for (int i = 0; i < references.Count; i++)
                AddContentFile(root, references[i]);

            return root;
        }

        static List<ContentAudioReference> CollectContentAudioReferences()
        {
            var references = new Dictionary<string, ContentAudioReference>(StringComparer.OrdinalIgnoreCase);

            if (MyDefinitionManager.Static == null)
                return new List<ContentAudioReference>();

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null)
                    continue;

                AudioWavesDefinition data = null;
                try
                {
                    data = definition;
                }
                catch (Exception error)
                {
                    LogHelper.Log(
                        MyLogSeverity.Warning,
                        "Could not inspect game audio definition " + definition.Id.SubtypeName + ": " + error.Message);
                    continue;
                }

                if (data == null || data.Waves == null)
                    continue;

                for (int i = 0; i < data.Waves.Count; i++)
                {
                    var wave = data.Waves[i];
                    if (wave == null)
                        continue;

                    AddContentReference(references, wave.Start, definition.Id.SubtypeName, "Start");
                    AddContentReference(references, wave.Loop, definition.Id.SubtypeName, "Loop");
                    AddContentReference(references, wave.End, definition.Id.SubtypeName, "End");
                }
            }

            var list = new List<ContentAudioReference>(references.Values);
            list.Sort(delegate(ContentAudioReference left, ContentAudioReference right)
            {
                return string.Compare(left == null ? null : left.Path, right == null ? null : right.Path, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        static void AddContentReference(
            Dictionary<string, ContentAudioReference> references,
            string path,
            string soundSubtype,
            string waveSlot)
        {
            path = NormalizeDefinitionPath(path);
            if (string.IsNullOrEmpty(path))
                return;

            if (!GameAudioPcmLoader.IsSupportedAudioPath(path))
                return;

            ContentAudioReference reference;
            if (!references.TryGetValue(path, out reference))
            {
                reference = new ContentAudioReference
                {
                    Path = path,
                    FirstSoundSubtype = soundSubtype,
                    FirstWaveSlot = waveSlot,
                    ReferenceCount = 0
                };
                references.Add(path, reference);
            }

            reference.ReferenceCount++;
        }

        static void AddContentFile(FolderModel root, ContentAudioReference reference)
        {
            if (root == null || reference == null || string.IsNullOrEmpty(reference.Path))
                return;

            var parts = reference.Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            var folder = root;
            for (int i = 0; i < parts.Length - 1; i++)
                folder = GetOrCreateFolder(folder, parts[i]);

            var fileName = parts[parts.Length - 1];
            folder.Files.Add(new FileModel
            {
                Name = fileName,
                FullPath = MediaAudioFileReference.SOURCE_CONTENT + "/" + reference.Path,
                Subtitle = BuildContentSubtitle(reference),
                Tag = new MediaAudioFileReference
                {
                    Source = MediaAudioFileReference.SOURCE_CONTENT,
                    DefinitionPath = reference.Path,
                    GameContentPath = GameAudioPcmLoader.ToAudioGameContentPath(reference.Path),
                    FirstSoundSubtype = reference.FirstSoundSubtype,
                    FirstWaveSlot = reference.FirstWaveSlot,
                    ReferenceCount = reference.ReferenceCount
                }
            });
        }

        static FolderModel GetOrCreateFolder(FolderModel parent, string name)
        {
            if (parent.Folders == null)
                throw new InvalidOperationException("Folder list missing.");

            for (int i = 0; i < parent.Folders.Count; i++)
            {
                var existing = parent.Folders[i];
                if (existing != null && string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            var folder = new FolderModel
            {
                Name = name,
                Subtitle = parent.FullPath
            };
            parent.Folders.Add(folder);
            return folder;
        }

        static AudioLibraryMetadata LoadAudioMetadata()
        {
            return AudioLibraryStorage.LoadMetadata();
        }

        static string NormalizeDefinitionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim();
            while (path.StartsWith("/", StringComparison.Ordinal) ||
                   path.StartsWith("\\", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            path = path.Replace('\\', '/');
            if (path.StartsWith("Audio/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("Audio/".Length);

            return path;
        }

        static string GetDisplayFileName(AudioAssetMetadata asset)
        {
            if (asset == null)
                return string.Empty;

            var source = asset.SourcePath;
            if (!string.IsNullOrWhiteSpace(source))
                return Path.GetFileName(source.Replace('\\', '/'));

            var runtime = asset.RuntimePath;
            if (!string.IsNullOrWhiteSpace(runtime))
                return Path.GetFileName(runtime.Replace('\\', '/'));

            return string.IsNullOrWhiteSpace(asset.Id) ? "audio.wav" : asset.Id + ".wav";
        }

        static string BuildLocalSubtitle(AudioAssetMetadata asset)
        {
            if (asset == null)
                return string.Empty;

            var duration = asset.DurationTicks > 0
                ? TimeSpan.FromTicks(asset.DurationTicks).TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s"
                : "unknown length";
            var format = asset.SampleRate > 0 && asset.Channels > 0
                ? asset.SampleRate.ToString(CultureInfo.InvariantCulture) + " Hz " + asset.Channels.ToString(CultureInfo.InvariantCulture) + "ch WAV"
                : "local WAV";
            return duration + " · " + format;
        }

        static string BuildContentSubtitle(ContentAudioReference reference)
        {
            if (reference == null)
                return string.Empty;

            var format = GameAudioPcmLoader.GetContainerDisplayName(GameAudioPcmLoader.GetContainerKind(reference.Path));
            var owner = string.IsNullOrEmpty(reference.FirstSoundSubtype)
                ? "definition audio"
                : reference.FirstSoundSubtype + "." + reference.FirstWaveSlot;
            if (reference.ReferenceCount > 1)
                owner += " (+" + (reference.ReferenceCount - 1).ToString(CultureInfo.InvariantCulture) + ")";
            return format + " · " + owner;
        }
    }
}
#endif
