using LcdMod.Common.Helpers;

namespace LcdMod.Client.Helpers
{
    internal static class ButtonPadLocalization
    {
        const string PREFIX = Constants.MOD_PREFIX + "ButtonPad_";

        static string Get(string suffix)
        {
            return LocHelper.GetLoc(PREFIX + suffix);
        }

        static string Format(string suffix, params object[] args)
        {
            return string.Format(FormatingHelper.Culture, Get(suffix), args);
        }

        public static string ActionUnavailable => Get("ActionUnavailable");
        public static string NoCompatibleTarget => Get("NoCompatibleTarget");
        public static string ActionFailed => Get("ActionFailed");
        public static string ButtonLabel => Get("Button");
        public static string Button(int oneBasedIndex) => Format("ButtonFormat", oneBasedIndex);
        public static string Title => Get("Title");
        public static string TitleHelp => Get("TitleHelp");
        public static string Apply => Get("Apply");
        public static string Delete => Get("Delete");
        public static string SelectTarget => Get("SelectTarget");
        public static string Target(string targetName) => Format("TargetFormat", targetName);
        public static string SelectAction => Get("SelectAction");
        public static string Action(string actionName) => Format("ActionFormat", actionName);
        public static string ActionValue(string actionName, string value) => Format("ActionValueFormat", actionName, value);
        public static string ButtonColor => Get("ButtonColor");
        public static string Color => Get("Color");
        public static string ColorHexHelp => Get("ColorHexHelp");
        public static string InvalidColor => Get("InvalidColor");

        public static string TargetDialogTitle => Get("TargetDialog_Title");
        public static string TargetDialogSearchTitle => Get("TargetDialog_SearchTitle");
        public static string TargetDialogSearchPlaceholder => Get("TargetDialog_SearchPlaceholder");
        public static string TargetDialogSearchHelp => Get("TargetDialog_SearchHelp");
        public static string TargetDialogNoTargets => Get("TargetDialog_NoTargets");
        public static string NoMatches => Get("NoMatches");
        public static string TargetKindBlock => Get("TargetKind_Block");
        public static string TargetKindGroup => Get("TargetKind_Group");
        public static string TargetKindBlockType => Get("TargetKind_BlockType");
        public static string TargetKindBlockSubtype => Get("TargetKind_BlockSubtype");

        public static string ActionDialogSearchTitle => Get("ActionDialog_SearchTitle");
        public static string ActionDialogSearchPlaceholder => Get("ActionDialog_SearchPlaceholder");
        public static string ActionDialogSearchHelp => Get("ActionDialog_SearchHelp");
        public static string ActionDialogNoCompatibleActions => Get("ActionDialog_NoCompatibleActions");

        public static string ConfigureActionTitle => Get("ConfigureAction_Title");
        public static string ConfigureActionScroll => Get("ConfigureAction_Scroll");
        public static string ConfigureActionNoParameters => Get("ConfigureAction_NoParameters");
        public static string ConfigureActionEnterValue => Get("ConfigureAction_EnterValue");
        public static string ConfigureActionColorHexHelp => Get("ConfigureAction_ColorHexHelp");
        public static string ConfigureActionParameter => Get("ConfigureAction_Parameter");
        public static string ConfigureActionNumberValue => Get("ConfigureAction_NumberValue");
        public static string ConfigureActionTextValue => Get("ConfigureAction_TextValue");
        public static string ConfigureActionColorValue => Get("ConfigureAction_ColorValue");
        public static string ConfigureActionBooleanMode => Get("ConfigureAction_BooleanMode");
        public static string ConfigureActionClickAction => Get("ConfigureAction_ClickAction");
        public static string ConfigureActionOn => Get("ConfigureAction_On");
        public static string ConfigureActionOff => Get("ConfigureAction_Off");
        public static string ConfigureActionToggle => Get("ConfigureAction_Toggle");
        public static string ConfigureActionDecrease => Get("ConfigureAction_Decrease");
        public static string ConfigureActionIncrease => Get("ConfigureAction_Increase");
        public static string ConfigureActionNormal => Get("ConfigureAction_Normal");
        public static string ConfigureActionReversed => Get("ConfigureAction_Reversed");
        public static string ConfigureActionNone => Get("ConfigureAction_None");
        public static string ConfigureActionWithScroll(string primary, string scrollMode) =>
            Format("ConfigureAction_ScrollFormat", primary, scrollMode);
    }
}
