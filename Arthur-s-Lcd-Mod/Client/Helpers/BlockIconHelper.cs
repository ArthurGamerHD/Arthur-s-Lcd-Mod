using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using VRage.Game;
using VRage.ObjectBuilders;

namespace LcdMod.Client.Helpers
{
    public static class BlockIconHelper
    {
        static readonly HashSet<MyCubeBlockDefinition> HashSet = new HashSet<MyCubeBlockDefinition>();
        
        public static void PreloadAllTextures()
        {
            var sb = new StringBuilder();
            var line = new StringBuilder();

            foreach (var myDefinitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var definition = myDefinitionBase as MyCubeBlockDefinition;
                if (definition != null && !HashSet.Contains(definition)) 
                    line.Append(GetOrAddTextureForBlock(definition)+ ", ");
                
                if (line.Length > 160)
                {
                    sb.AppendLine(line.ToString());
                    line.Clear();
                }
            }

            sb.AppendLine(line.ToString());
            var textures = sb.ToString().TrimEnd('\n',',');
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

        static MyLCDTextureDefinition CreateLcdTextureDefinition(MyCubeBlockDefinition blockDefinition)
        {
            MyLCDTextureDefinition textureDefinition = new MyLCDTextureDefinition
            {
                Id = new MyDefinitionId((MyObjectBuilderType) typeof (MyObjectBuilder_LCDTextureDefinition), blockDefinition.Id.ToString()),
                Public = false,
                LocalizationId = blockDefinition.DisplayNameString,
                SpritePath = blockDefinition.Icons.Length != 0 ? blockDefinition.Icons[0] : string.Empty,
                Selectable = false
            };

            return textureDefinition;
        }
    }
}
