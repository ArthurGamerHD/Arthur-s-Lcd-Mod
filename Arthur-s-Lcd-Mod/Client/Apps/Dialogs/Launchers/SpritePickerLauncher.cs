using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using LcdMod.Client.Images;
using LcdMod.Common.Images;
using Sandbox.ModAPI;
using VRage.Input;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Apps.Dialogs.Launchers
{
    /// <summary>
    /// Image picker on the shared file picker: the player's own images, the surface's LCD sprites, and block icons,
    /// each grouped into folders by the path segments of the sprite name. File tags are the sprite names.
    /// </summary>
    internal static class SpritePickerLauncher
    {
        const string OBJECT_BUILDER_PREFIX = "MyObjectBuilder_";

        static string _currentPath;

        public static bool PickOne(App app, IMyTextSurface surface, Action<string> picked)
        {
            var model = new FilePickerViewModel(
                LocHelper.GetLoc("LcdMod_SpritePicker_Title"),
                FilePickerMode.PickFile,
                BuildRoots(surface),
                result =>
                {
                    var sprite = result.Tag as string;
                    if (sprite != null && picked != null)
                        picked(sprite);
                },
                CurrentPath,
                SetCurrentPath,
                BuildActions);
            return Show(app, model, surface);
        }

        public static bool PickMany(App app, IMyTextSurface surface, IEnumerable<string> selected, Action<string[]> applied)
        {
            var model = FilePickerViewModel.Multiple(
                LocHelper.GetLoc("LcdMod_SpritePicker_Title"),
                BuildRoots(surface),
                selected,
                applied,
                CurrentPath,
                SetCurrentPath,
                BuildActions);
            return Show(app, model, surface);
        }

        static string CurrentPath => _currentPath ?? LocHelper.GetLoc("LcdMod_SpritePicker_Root_Local");

        static void SetCurrentPath(string path)
        {
            _currentPath = path;
        }

        static bool Show(App app, FilePickerViewModel model, IMyTextSurface surface)
        {
            model.IsToggleModifierDown = () => IsDown(MyKeys.Control, MyKeys.LeftControl, MyKeys.RightControl);
            model.IsRangeModifierDown = () => IsDown(MyKeys.Shift, MyKeys.LeftShift, MyKeys.RightShift);
            if (!app.ShowDialog(new FilePickerView(model)))
                return false;

            var library = LocalImageLibrary.Instance;
            if (library == null)
                return true;

            Action changed = null;
            changed = delegate
            {
                if (model.IsClosed)
                {
                    library.Changed -= changed;
                    return;
                }

                model.RefreshRoots(BuildRoots(surface));
                model.SetBusy(library.IsScanning);
            };
            library.Changed += changed;
            library.Rescan(false);
            model.SetBusy(library.IsScanning);
            return true;
        }

        static List<FilePickerContextAction> BuildActions(FilePickerResult result)
        {
            var library = LocalImageLibrary.Instance;
            var sprite = result == null ? null : result.Tag as string;
            string name;
            if (library == null || result.File == null || sprite == null || !library.TryGetDisplayName(sprite, out name))
                return null;

            return new List<FilePickerContextAction>
            {
                new FilePickerContextAction(LocHelper.GetLoc("LcdMod_SpritePicker_RemoveFromLibrary"), delegate { library.Remove(name); })
                {
                    UseErrorTextStyle = true
                }
            };
        }

        static List<FolderModel> BuildRoots(IMyTextSurface surface)
        {
            var local = new FolderModel { Name = LocHelper.GetLoc("LcdMod_SpritePicker_Root_Local") };
            var library = LocalImageLibrary.Instance;
            if (library != null)
            {
                for (int i = 0; i < library.Items.Count; i++)
                {
                    var item = library.Items[i];
                    string sprite = GridImageCodec.ToSpriteName(item.Image.Id);
                    local.Files.Add(new FileModel
                    {
                        Name = item.Name,
                        Tag = sprite,
                        Icon = sprite,
                        Subtitle = item.Image.Width + " x " + item.Image.Height
                    });
                }
            }

            var roots = new List<FolderModel> { local };

            var names = new List<string>();
            if (surface != null)
                surface.GetSprites(names);
            AddRoot(roots, "LcdMod_SpritePicker_Root_Sprites", ByDisplayPath(names));

            names.Clear();
            TextureHelper.GetBlockSpriteNames(names);
            AddRoot(roots, "LcdMod_SpritePicker_Root_Blocks", ByDisplayPath(names));

            var generated = new Dictionary<string, string>(StringComparer.Ordinal);
            TextureHelper.GetGeneratedSprites(generated);
            AddRoot(roots, "LcdMod_MarketApp", new List<KeyValuePair<string, string>>(generated));

            return roots;
        }

        static List<KeyValuePair<string, string>> ByDisplayPath(List<string> sprites)
        {
            var result = new List<KeyValuePair<string, string>>(sprites.Count);
            for (int i = 0; i < sprites.Count; i++)
            {
                if (!string.IsNullOrEmpty(sprites[i]))
                    result.Add(new KeyValuePair<string, string>(DisplayPath(sprites[i]), sprites[i]));
            }

            return result;
        }

        static void AddRoot(List<FolderModel> roots, string nameKey, List<KeyValuePair<string, string>> spritesByPath)
        {
            var root = new FolderModel { Name = LocHelper.GetLoc(nameKey) };
            var folders = new Dictionary<string, FolderModel>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < spritesByPath.Count; i++)
            {
                string sprite = spritesByPath[i].Value;
                if (string.IsNullOrEmpty(sprite) || !seen.Add(sprite))
                    continue;

                string[] segments = spritesByPath[i].Key.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                    continue;

                FolderModel folder = root;
                string path = string.Empty;
                for (int s = 0; s < segments.Length - 1; s++)
                {
                    path += "/" + segments[s];
                    FolderModel child;
                    if (!folders.TryGetValue(path, out child))
                    {
                        child = new FolderModel { Name = segments[s] };
                        folders[path] = child;
                        folder.Folders.Add(child);
                    }

                    folder = child;
                }

                folder.Files.Add(new FileModel
                {
                    Name = segments[segments.Length - 1],
                    Tag = sprite,
                    Icon = sprite
                });
            }

            if (root.Files.Count > 0 || root.Folders.Count > 0)
                roots.Add(root);
        }

        static string DisplayPath(string sprite)
        {
            return sprite.StartsWith(OBJECT_BUILDER_PREFIX, StringComparison.Ordinal)
                ? sprite.Substring(OBJECT_BUILDER_PREFIX.Length)
                : sprite;
        }

        static bool IsDown(MyKeys any, MyKeys left, MyKeys right)
        {
            var input = MyAPIGateway.Input;
            return input.IsKeyPress(any) || input.IsKeyPress(left) || input.IsKeyPress(right);
        }
    }
}
