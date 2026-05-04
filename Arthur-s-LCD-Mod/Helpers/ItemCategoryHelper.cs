using VRage;

namespace Graph.Helpers
{
    public class ItemCategoryHelper
    {
        const string CATEGORY_REGEX = @"_(.*?)\\";
        
        public static string[] Groups = { "AmmoMagazine", "Component", "PhysicalGun", "Ingot", "Ore", "ConsumableItem", "SeedItem" };
        
        public static string GetGroupName(string groupName)
        {
            switch (groupName)
            {
                case "AmmoMagazine":
                    return MyTexts.GetString("DisplayName_ConvSorterTypes_Ammo");
                case "Component":
                    return MyTexts.GetString("DisplayName_ConvSorterTypes_Component");
                case "PhysicalGun":
                    return MyTexts.GetString("DisplayName_ConvSorterTypes_HandTool");    
                case "Ingot":
                    return MyTexts.GetString("DisplayName_ConvSorterTypes_Ingot");
                case "Ore":
                    return MyTexts.GetString("DisplayName_ConvSorterTypes_Ore");
                case "ConsumableItem":
                    return $"*{MyTexts.GetString("DisplayName_BlueprintClass_Consumables").ToLower()}*";
                case "SeedItem":
                    return $"*{MyTexts.GetString("DisplayName_BlueprintClass_GardenItems").ToLower()}*";
            }

            return groupName;
        }
        
        public static string GetGroupDisplayName(string groupName)
        {
            switch (groupName)
            {
                case "AmmoMagazine":
                    return MyTexts.GetString("DisplayName_BlueprintClass_Ammo");
                case "Component":
                    return MyTexts.GetString("DisplayName_BlueprintClass_Components");
                case "PhysicalGun":
                    return MyTexts.GetString("DisplayName_BlueprintClass_Tools");    
                case "Ingot":
                    return MyTexts.GetString("Description_BlueprintClass_Ingots");
                case "Ore":
                    return MyTexts.GetString("RadialMenuGroupTitle_VoxelOres");
                case "ConsumableItem":
                    return MyTexts.GetString("DisplayName_BlueprintClass_Consumables");
                case "SeedItem":
                    return MyTexts.GetString("DisplayName_BlueprintClass_GardenItems");
            }

            return groupName;
        }
    }
}