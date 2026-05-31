using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Helpers
{
    public static class TextureHelper
    {
        static readonly HashSet<MyCubeBlockDefinition> HashSet = new HashSet<MyCubeBlockDefinition>();
        static readonly HashSet<string> CustomTextures = new HashSet<string>();

        public static void PreloadAllTextures()
        {
            var sb = new StringBuilder();
            var line = new StringBuilder();

            foreach (var myDefinitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var definition = myDefinitionBase as MyCubeBlockDefinition;
                if (definition != null && !HashSet.Contains(definition))
                    line.Append(GetOrAddTextureForBlock(definition) + ", ");

                if (line.Length > 160)
                {
                    sb.AppendLine(line.ToString());
                    line.Clear();
                }
            }

            sb.AppendLine(line.ToString());
            var textures = sb.ToString().TrimEnd('\n', ',');
            LogHelper.LogInfo($"Added new Sprite textures for blocks: {{\n{textures}\n}}");
        }

        public static string GetOrAddTextureForBlock(MyCubeBlockDefinition definition)
        {
            if (!HashSet.Add(definition))
                return definition.Id.ToString();

            var texture = CreateLcdTextureDefinition(definition);
            MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(texture);
            return texture.Id.SubtypeName;
        }


        public static void GetRegisteredSpriteNames(List<string> spriteNames)
        {
            if (spriteNames == null)
                return;

            foreach (var definition in HashSet)
            {
                if (definition == null)
                    continue;

                var spriteName = definition.Id.ToString();
                if (!string.IsNullOrEmpty(spriteName))
                    spriteNames.Add(spriteName);
            }

            foreach (var texture in CustomTextures)
            {
                if (texture == null)
                    continue;

                if (!string.IsNullOrEmpty(texture))
                    spriteNames.Add(texture);
            }
        }

        public static bool TryGetOrAddTextureForBlockName(string blockDefinitionName, out string textureName)
        {
            textureName = blockDefinitionName;
            if (string.IsNullOrEmpty(blockDefinitionName))
                return false;

            foreach (var definitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var blockDefinition = definitionBase as MyCubeBlockDefinition;
                if (blockDefinition == null)
                    continue;

                if (!string.Equals(blockDefinition.Id.ToString(), blockDefinitionName, StringComparison.Ordinal))
                    continue;

                textureName = GetOrAddTextureForBlock(blockDefinition);
                return true;
            }

            return false;
        }

        public static string ResolveItemSprite(MyPhysicalItemDefinition definition, IMyTextSurface surface)
        {
            if (definition == null)
                return string.Empty;

            var spriteNames = new List<string>();
            if (surface != null)
                surface.GetSprites(spriteNames);

            var itemId = definition.Id.ToString();
            var colorfulIcon = GetColorfulItemIconName(itemId);
            if (!string.IsNullOrEmpty(colorfulIcon) && spriteNames.Contains(colorfulIcon))
                return colorfulIcon;

            if (spriteNames.Contains(itemId))
                return itemId;

            if (definition.Icons != null && definition.Icons.Length > 0 && !string.IsNullOrEmpty(definition.Icons[0]))
                return definition.Icons[0];

            return itemId;
        }

        static string GetColorfulItemIconName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return string.Empty;

            const string prefix = "MyObjectBuilder_";
            if (!itemId.StartsWith(prefix, StringComparison.Ordinal))
                return string.Empty;

            return "ColorfulIcons_" + itemId.Substring(prefix.Length);
        }

        static MyLCDTextureDefinition CreateLcdTextureDefinition(MyCubeBlockDefinition blockDefinition)
        {
            MyLCDTextureDefinition textureDefinition = new MyLCDTextureDefinition
            {
                Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition),
                    blockDefinition.Id.ToString()),
                Public = false,
                LocalizationId = blockDefinition.DisplayNameString,
                SpritePath = blockDefinition.Icons.Length != 0 ? blockDefinition.Icons[0] : string.Empty,
                Selectable = false
            };

            return textureDefinition;
        }

        public static void LocalTexture(string[] obj)
        {
            ExportConverter();

            if (obj.Length == 1)
            {
                var id = obj[0];
                id = Path.GetFileNameWithoutExtension(id);

                var name = id + ".dds";

                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(name, typeof(LcdModClientComponent)))
                {
                    var scope = MyAPIGateway.Utilities.GamePaths.ModScopeName;
                    var path = Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Storage", scope, name);
                    path = path.Replace("/", "\\");

                    MyLCDTextureDefinition textureDefinition = new MyLCDTextureDefinition
                    {
                        Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition), id),
                        Public = false,
                        LocalizationId = name,
                        SpritePath = path,
                        TexturePath = path,
                        Selectable = true,
                        AvailableInSurvival = true
                    };
                    MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(textureDefinition);
                    CustomTextures.Add(id);
                    MyAPIGateway.Utilities.ShowNotification($"Definition created for texture {id}");
                }
                else
                {
                    MyAPIGateway.Utilities.ShowNotification($"File {name} does not exists in mod storage");
                }
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid argument");
            }
        }

        public static void ExportConverter()
        {
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("png-to-dds.sh", typeof(LcdModClientComponent)))
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage("png-to-dds.sh",
                    typeof(LcdModClientComponent));
                file.WriteLine(@"#!/usr/bin/env bash
set -euo pipefail

if [ ! -f tools_path.txt ]; then
  echo ""tools_path.txt not found"" >&2
  exit 1
fi

path=""$(awk 'NF{print; exit}' tools_path.txt)""
# Remove surrounding quotes and trailing slash if present
path=""${path%\""}""
path=""${path#\""}""
# Ensure no trailing slash required for wine invocation (wine accepts either)
# Run with wine
wine ""${path}\texconv.exe"" ./*.png -nologo -y -f BC7_UNORM -pmalpha 2

for file in ./*.DDS; do
    [ -e ""$file"" ] || continue

    destination=""$(printf '%s' ""$file"" | tr '[:upper:]' '[:lower:]')""
    temporary=""${file}.rename-tmp-$$""

    mv -- ""$file"" ""$temporary"" &&
        mv -- ""$temporary"" ""$destination""
done
");
                file.Flush();
                file.Close();
            }

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("png-to-dds.bat", typeof(LcdModClientComponent)))
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage("png-to-dds.bat",
                    typeof(LcdModClientComponent));
                file.WriteLine($@"
@echo off
setlocal enabledelayedexpansion
rem Read first non-empty line from tools_path.txt into PATHVAR
rem %%~A ja remove as aspas ao redor automaticamente
set ""PATHVAR=""
for /f ""usebackq delims="" %%A in (""tools_path.txt"") do (
  if not defined PATHVAR set ""PATHVAR=%%~A""
)
if not defined PATHVAR (
  echo tools_path.txt is empty or missing.
  exit /b 1
)
rem Ensure path ends with backslash
if not ""%PATHVAR:~-1%""==""\"" set ""PATHVAR=%PATHVAR%\""
rem Run texconv.exe from the game's own path on all PNGs in current directory
""%PATHVAR%/texconv.exe"" .\*.png -nologo -y -f BC7_UNORM -pmalpha
exit /b %ERRORLEVEL%
");
                file.Flush();
                file.Close();
            }
            
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("tools_path.txt", typeof(LcdModClientComponent)))
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage("tools_path.txt",
                    typeof(LcdModClientComponent));

                var content = MyAPIGateway.Utilities.GamePaths.ContentPath;

                content = content.Replace("Content", "Tools\\TexturePacking\\Tools");

                if (content.StartsWith("z", StringComparison.InvariantCultureIgnoreCase))
                    content = content.Substring(2).Replace("\\", "/");

                file.WriteLine(content);
                file.Flush();
                file.Close();
            }
        }
    }
}