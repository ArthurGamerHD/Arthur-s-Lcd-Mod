using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    sealed class MediaAudioFileReference
    {
        public const string SOURCE_LOCAL = "Local";
        public const string SOURCE_SOUND_BLOCK = "SoundBlock";
        public const string SOURCE_CONTENT = "Content";

        public string Source { get; set; }
        public string DefinitionPath { get; set; }
        public string GameContentPath { get; set; }
        public string FirstSoundSubtype { get; set; }
        public string FirstWaveSlot { get; set; }
        public string PickerFullPath { get; set; }
        public string PickerFolderPath { get; set; }
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

        public bool IsSoundBlock
        {
            get { return string.Equals(Source, SOURCE_SOUND_BLOCK, StringComparison.OrdinalIgnoreCase); }
        }
    }

    sealed class MediaAudioPlaylistReference
    {
        public const string SOURCE_PLAYLISTS = "Playlists";

        public string FileName { get; set; }
        public string DisplayName { get; set; }
        public string PickerFullPath { get; set; }
    }

    static class MediaAudioFilePickerTreeProvider
    {
        const string PLAYLIST_INDEX_FILE = "music_cache_playlists.txt";
        const string PLAYLIST_SAVE_FILE_PREFIX = "music_cache_playlist_";
        const string FAVORITES_PLAYLIST_FILE = "music_cache_favorites.m3u";
        const string PLAYLIST_FILE_EXTENSION = ".m3u";

        static string _currentPath;
        static List<FolderModel> _cachedRoots;

        sealed class ContentAudioReference
        {
            public string Path;
            public string DisplayName;
            public string FirstSoundSubtype;
            public string FirstWaveSlot;
            public int ReferenceCount;
        }

        sealed class SoundCategoryNameLookup
        {
            public readonly Dictionary<string, string> BySoundId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> ByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static List<FolderModel> BuildRoots()
        {
            var roots = new List<FolderModel>
            {
                BuildLocalRoot(),
                BuildPlaylistRoot(),
                BuildSoundBlockRoot(),
                BuildContentRoot()
            };
            _cachedRoots = roots;
            return roots;
        }

        public static void BuildRootsAsync(Action<List<FolderModel>, Exception> completed)
        {
            List<FolderModel> roots = null;
            Exception failure = null;

            MyAPIGateway.Parallel.Start(
                delegate
                {
                    try
                    {
                        roots = BuildRoots();
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                },
                delegate
                {
                    if (completed != null)
                        completed(roots, failure);
                });
        }

        public static string CurrentPath
        {
            get { return _currentPath ?? string.Empty; }
        }

        public static void SetCurrentPath(string path)
        {
            _currentPath = NormalizePickerPath(path);
        }

        public static List<FolderModel> GetCachedRootsOrBuild()
        {
            return _cachedRoots ?? BuildRoots();
        }

        public static void InvalidateCache()
        {
            _cachedRoots = null;
        }

        public static bool TryDeleteLocalAudio(MediaAudioFileReference reference, out string failureReason)
        {
            failureReason = string.Empty;

            if (reference == null || !reference.IsLocal)
            {
                failureReason = "Only imported local audio can be deleted.";
                return false;
            }

            if (reference.LocalAsset == null)
            {
                failureReason = "Missing local audio metadata.";
                return false;
            }

            var deleted = AudioLibraryStorage.TryDeleteAsset(reference.LocalAsset, out failureReason);
            if (deleted)
                InvalidateCache();
            return deleted;
        }

        public static bool TryDeletePlaylist(MediaAudioPlaylistReference playlist, out string failureReason)
        {
            failureReason = string.Empty;

            if (playlist == null || !IsPlaylistFileName(playlist.FileName))
            {
                failureReason = "Invalid playlist file.";
                return false;
            }

            if (MyAPIGateway.Utilities == null)
            {
                failureReason = "Local storage is not available.";
                return false;
            }

            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(playlist.FileName, typeof(LcdModClientComponent)))
                {
                    RemovePlaylistIndexRecord(playlist.FileName);
                    InvalidateCache();
                    return true;
                }

                MyAPIGateway.Utilities.DeleteFileInLocalStorage(playlist.FileName, typeof(LcdModClientComponent));
                RemovePlaylistIndexRecord(playlist.FileName);
                InvalidateCache();
                return true;
            }
            catch (Exception error)
            {
                failureReason = "Could not delete playlist: " + error.Message;
                LogHelper.Log(MyLogSeverity.Warning, failureReason);
                return false;
            }
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
                var fullPath = MediaAudioFileReference.SOURCE_LOCAL + "/" + fileName;
                root.Files.Add(new FileModel
                {
                    Name = fileName,
                    FullPath = fullPath,
                    IconPath = fileName,
                    Subtitle = BuildLocalSubtitle(asset),
                    Tag = new MediaAudioFileReference
                    {
                        Source = MediaAudioFileReference.SOURCE_LOCAL,
                        DefinitionPath = fileName,
                        GameContentPath = asset.RuntimePath,
                        PickerFullPath = fullPath,
                        PickerFolderPath = root.FullPath,
                        LocalAsset = asset,
                        ReferenceCount = 1
                    }
                });
            }

            return root;
        }

        static FolderModel BuildPlaylistRoot()
        {
            var root = new FolderModel
            {
                Name = MediaAudioPlaylistReference.SOURCE_PLAYLISTS,
                FullPath = MediaAudioPlaylistReference.SOURCE_PLAYLISTS,
                Subtitle = "Saved local music playlists"
            };

            AddPlaylistFileIfExists(root, FAVORITES_PLAYLIST_FILE, "Favorites");

            var records = LoadPlaylistIndex();
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                AddPlaylistFileIfExists(root, record.FileName, record.DisplayName);
            }

            return root;
        }

        sealed class PlaylistIndexRecord
        {
            public string FileName;
            public string DisplayName;
        }

        static List<PlaylistIndexRecord> LoadPlaylistIndex()
        {
            var records = new List<PlaylistIndexRecord>();
            if (MyAPIGateway.Utilities == null ||
                !MyAPIGateway.Utilities.FileExistsInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
                return records;

            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
                {
                    if (reader == null)
                        return records;

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0)
                            continue;

                        var separator = line.IndexOf('|');
                        var fileName = separator < 0 ? line : line.Substring(0, separator).Trim();
                        var displayName = separator < 0 ? GetPlaylistDisplayNameFromFileName(fileName) : line.Substring(separator + 1).Trim();
                        if (IsPlaylistFileName(fileName))
                        {
                            records.Add(new PlaylistIndexRecord
                            {
                                FileName = fileName,
                                DisplayName = string.IsNullOrWhiteSpace(displayName) ? GetPlaylistDisplayNameFromFileName(fileName) : displayName
                            });
                        }
                    }
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read media playlist index: " + error.Message);
            }

            return records;
        }

        static void RemovePlaylistIndexRecord(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            var records = LoadPlaylistIndex();
            var removed = false;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];
                if (record == null || !string.Equals(record.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                records.RemoveAt(i);
                removed = true;
            }

            if (removed)
                SavePlaylistIndex(records);
        }

        static void SavePlaylistIndex(List<PlaylistIndexRecord> records)
        {
            if (MyAPIGateway.Utilities == null)
                return;

            try
            {
                if (records == null || records.Count == 0)
                {
                    if (MyAPIGateway.Utilities.FileExistsInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
                        MyAPIGateway.Utilities.DeleteFileInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent));
                    return;
                }

                var builder = new StringBuilder();
                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    if (record == null || !IsPlaylistFileName(record.FileName))
                        continue;

                    builder.Append(record.FileName)
                        .Append('|')
                        .AppendLine((record.DisplayName ?? string.Empty).Replace('|', ' '));
                }

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
                {
                    writer.Write(builder.ToString());
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not update media playlist index: " + error.Message);
            }
        }

        static void AddPlaylistFileIfExists(FolderModel root, string fileName, string displayName)
        {
            if (root == null || string.IsNullOrWhiteSpace(fileName) || MyAPIGateway.Utilities == null)
                return;

            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModClientComponent)))
                    return;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = GetPlaylistDisplayNameFromFileName(fileName);

            var pickerPath = MediaAudioPlaylistReference.SOURCE_PLAYLISTS + "/" + displayName + PLAYLIST_FILE_EXTENSION;
            root.Files.Add(new FileModel
            {
                Name = displayName + PLAYLIST_FILE_EXTENSION,
                FullPath = pickerPath,
                IconPath = "playlist" + PLAYLIST_FILE_EXTENSION,
                Subtitle = "M3U playlist",
                Tag = new MediaAudioPlaylistReference
                {
                    FileName = fileName,
                    DisplayName = displayName,
                    PickerFullPath = pickerPath
                }
            });
        }

        static bool IsPlaylistFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                   fileName.EndsWith(PLAYLIST_FILE_EXTENSION, StringComparison.OrdinalIgnoreCase);
        }

        static string GetPlaylistDisplayNameFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Playlist";

            var name = fileName.Trim();
            if (name.StartsWith(PLAYLIST_SAVE_FILE_PREFIX, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(PLAYLIST_SAVE_FILE_PREFIX.Length);
            if (name.EndsWith(PLAYLIST_FILE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - PLAYLIST_FILE_EXTENSION.Length);

            return string.IsNullOrWhiteSpace(name) ? "Playlist" : name;
        }

        static FolderModel BuildSoundBlockRoot()
        {
            var root = new FolderModel
            {
                Name = MyTexts.GetString("DisplayName_Block_SoundBlock"),
                FullPath = MediaAudioFileReference.SOURCE_SOUND_BLOCK,
                Subtitle = "Sound block sounds"
            };

            if (MyDefinitionManager.Static == null)
                return root;

            var files = new List<FileModel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MySoundCategoryDefinition category in MyDefinitionManager.Static.GetSoundCategoryDefinitions())
            {
                if (category == null || !category.Public || category.Sounds == null)
                    continue;

                for (int i = 0; i < category.Sounds.Count; i++)
                {
                    var sound = category.Sounds[i];
                    if (sound == null || string.IsNullOrEmpty(sound.SoundId) || !seen.Add(sound.SoundId))
                        continue;

                    var definition = FindSoundDefinition(sound.SoundId);
                    var wavePath = FindStartWave(definition);
                    if (string.IsNullOrEmpty(wavePath))
                        continue;

                    var displayName = ResolveSoundBlockDisplayName(sound);
                    var fullPath = MediaAudioFileReference.SOURCE_SOUND_BLOCK + "/" + sound.SoundId;
                    files.Add(new FileModel
                    {
                        Name = displayName,
                        FullPath = fullPath,
                        IconPath = wavePath,
                        Subtitle = BuildSoundBlockSubtitle(sound, wavePath),
                        Tag = new MediaAudioFileReference
                        {
                            Source = MediaAudioFileReference.SOURCE_SOUND_BLOCK,
                            DefinitionPath = wavePath,
                            GameContentPath = GameAudioPcmLoader.ToAudioGameContentPath(wavePath),
                            FirstSoundSubtype = sound.SoundId,
                            FirstWaveSlot = "Start",
                            PickerFullPath = fullPath,
                            PickerFolderPath = root.FullPath,
                            ReferenceCount = 1
                        }
                    });
                }
            }

            files.Sort(delegate(FileModel left, FileModel right)
            {
                return string.Compare(left == null ? null : left.Name, right == null ? null : right.Name, StringComparison.OrdinalIgnoreCase);
            });
            root.Files.AddRange(files);
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

            var nameLookup = BuildSoundCategoryNameLookup();
            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null)
                    continue;

                AudioWavesDefinition data;
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

                    AddContentReference(references, nameLookup, wave.Start, definition.Id.SubtypeName, "Start");
                    AddContentReference(references, nameLookup, wave.Loop, definition.Id.SubtypeName, "Loop");
                    AddContentReference(references, nameLookup, wave.End, definition.Id.SubtypeName, "End");
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
            SoundCategoryNameLookup nameLookup,
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
                    DisplayName = ResolveSoundCategoryName(nameLookup, soundSubtype, path),
                    FirstSoundSubtype = soundSubtype,
                    FirstWaveSlot = waveSlot,
                    ReferenceCount = 0
                };
                references.Add(path, reference);
            }
            else if (string.IsNullOrEmpty(reference.DisplayName))
            {
                reference.DisplayName = ResolveSoundCategoryName(nameLookup, soundSubtype, path);
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
            var fullPath = MediaAudioFileReference.SOURCE_CONTENT + "/" + reference.Path;
            folder.Files.Add(new FileModel
            {
                Name = fileName,
                FullPath = fullPath,
                IconPath = reference.Path,
                Subtitle = BuildContentSubtitle(reference),
                Tag = new MediaAudioFileReference
                {
                    Source = MediaAudioFileReference.SOURCE_CONTENT,
                    DefinitionPath = reference.Path,
                    GameContentPath = GameAudioPcmLoader.ToAudioGameContentPath(reference.Path),
                    FirstSoundSubtype = reference.FirstSoundSubtype,
                    FirstWaveSlot = reference.FirstWaveSlot,
                    PickerFullPath = fullPath,
                    PickerFolderPath = folder.FullPath,
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
                FullPath = string.IsNullOrEmpty(parent.FullPath) ? name : parent.FullPath + "/" + name,
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

        static string NormalizePickerPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Trim().Replace('\\', '/').Trim('/');
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

            var title = reference.DisplayName;
            var format = GameAudioPcmLoader.GetContainerDisplayName(GameAudioPcmLoader.GetContainerKind(reference.Path));
            var owner = string.IsNullOrEmpty(reference.FirstSoundSubtype)
                ? "definition audio"
                : reference.FirstSoundSubtype + "." + reference.FirstWaveSlot;
            if (reference.ReferenceCount > 1)
                owner += " (+" + (reference.ReferenceCount - 1).ToString(CultureInfo.InvariantCulture) + ")";

            return string.IsNullOrEmpty(title)
                ? format + " · " + owner
                : title + " · " + format + " · " + owner;
        }

        static string BuildSoundBlockSubtitle(MySoundCategoryDefinition.SoundDescription sound, string wavePath)
        {
            var format = GameAudioPcmLoader.GetContainerDisplayName(GameAudioPcmLoader.GetContainerKind(wavePath));
            var soundId = sound == null ? string.Empty : sound.SoundId;
            return string.IsNullOrEmpty(soundId)
                ? format
                : format + " · " + soundId;
        }

        static string ResolveSoundBlockDisplayName(MySoundCategoryDefinition.SoundDescription sound)
        {
            if (sound == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(sound.SoundText))
                return sound.SoundText;

            if (!string.IsNullOrEmpty(sound.SoundName))
                return sound.SoundName;

            return sound.SoundId ?? string.Empty;
        }

        static MyAudioDefinition FindSoundDefinition(string subtype)
        {
            if (MyDefinitionManager.Static == null || string.IsNullOrEmpty(subtype))
                return null;

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition != null && string.Equals(definition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    return definition;
            }

            return null;
        }

        static SoundCategoryNameLookup BuildSoundCategoryNameLookup()
        {
            var lookup = new SoundCategoryNameLookup();
            if (MyDefinitionManager.Static == null)
                return lookup;

            foreach (MySoundCategoryDefinition category in MyDefinitionManager.Static.GetSoundCategoryDefinitions())
            {
                if (category == null || category.Sounds == null)
                    continue;

                for (int i = 0; i < category.Sounds.Count; i++)
                {
                    var sound = category.Sounds[i];
                    if (sound == null || string.IsNullOrEmpty(sound.SoundId))
                        continue;

                    var text = sound.SoundText;
                    if (string.IsNullOrEmpty(text))
                        text = sound.SoundName;
                    if (!string.IsNullOrEmpty(text) && !lookup.BySoundId.ContainsKey(sound.SoundId))
                        lookup.BySoundId.Add(sound.SoundId, text);
                }
            }

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id.SubtypeName))
                    continue;

                string text;
                if (!lookup.BySoundId.TryGetValue(definition.Id.SubtypeName, out text) || string.IsNullOrEmpty(text))
                    continue;

                AddFileNameLookup(lookup, FindStartWave(definition), text);
            }

            return lookup;
        }

        static string ResolveSoundCategoryName(SoundCategoryNameLookup lookup, string subtype, string path)
        {
            if (lookup == null)
                return string.Empty;

            string name;
            if (!string.IsNullOrEmpty(subtype) && lookup.BySoundId.TryGetValue(subtype, out name))
                return name;

            var fileName = GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(fileName) && lookup.ByFileName.TryGetValue(fileName, out name))
                return name;

            return string.Empty;
        }

        static void AddFileNameLookup(SoundCategoryNameLookup lookup, string path, string text)
        {
            if (lookup == null || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text))
                return;

            var fileName = GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName) || lookup.ByFileName.ContainsKey(fileName))
                return;

            lookup.ByFileName.Add(fileName, text);
        }

        static string FindStartWave(MyAudioDefinition definition)
        {
            if (definition == null)
                return null;

            AudioWavesDefinition data;
            try
            {
                data = definition;
            }
            catch
            {
                return null;
            }

            if (data == null || data.Waves == null)
                return null;

            for (int i = 0; i < data.Waves.Count; i++)
            {
                var wave = data.Waves[i];
                if (wave != null && !string.IsNullOrEmpty(wave.Start))
                    return NormalizeDefinitionPath(wave.Start);
            }

            return null;
        }

        static string GetFileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            var name = slash >= 0 && slash + 1 < normalized.Length
                ? normalized.Substring(slash + 1)
                : normalized;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }
    }
}
