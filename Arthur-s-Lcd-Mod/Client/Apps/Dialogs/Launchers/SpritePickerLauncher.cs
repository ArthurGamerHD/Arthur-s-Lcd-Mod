using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using VRage.Input;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Apps.Dialogs.Launchers
{
    internal static class SpritePickerLauncher
    {
        public static bool PickOne(App app, IMyTextSurface surface, Action<string> picked)
        {
            return Show(app, SpritePickerViewModel.Single(LoadSprites(surface), picked));
        }

        public static bool PickMany(App app, IMyTextSurface surface, IEnumerable<string> selected, Action<string[]> applied)
        {
            return Show(app, SpritePickerViewModel.Multiple(LoadSprites(surface), selected, applied));
        }

        static bool Show(App app, SpritePickerViewModel model)
        {
            model.IsToggleModifierDown = () => IsDown(MyKeys.Control, MyKeys.LeftControl, MyKeys.RightControl);
            model.IsRangeModifierDown = () => IsDown(MyKeys.Shift, MyKeys.LeftShift, MyKeys.RightShift);
            return app.ShowDialog(new SpritePickerView(model));
        }

        static List<string> LoadSprites(IMyTextSurface surface)
        {
            var registered = new List<string>();
            TextureHelper.GetRegisteredSpriteNames(registered);
            registered.Sort(StringComparer.OrdinalIgnoreCase);
            if (surface != null)
            {
                var lcd = new List<string>();
                surface.GetSprites(lcd);
                lcd.Sort(StringComparer.OrdinalIgnoreCase);
                registered.AddRange(lcd);
            }
            return registered;
        }

        static bool IsDown(MyKeys any, MyKeys left, MyKeys right)
        {
            var input = MyAPIGateway.Input;
            return input.IsKeyPress(any) || input.IsKeyPress(left) || input.IsKeyPress(right);
        }
    }
}
