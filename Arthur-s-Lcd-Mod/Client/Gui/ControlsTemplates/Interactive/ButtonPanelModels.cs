using System;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using ProtoBuf;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    [ProtoContract]
    sealed class ButtonPanelSettings
    {
        [ProtoMember(1)] public int EntryCount { get; set; }
        [ProtoMember(2)] public ButtonPanelEntrySettings[] Entries { get; set; }
    }

    [ProtoContract]
    sealed class ButtonPanelEntrySettings
    {
        [ProtoMember(1)] public int Index { get; set; }
        [ProtoMember(2)] public string Title { get; set; }
        [ProtoMember(3)] public string SpriteName { get; set; }
        [ProtoMember(4)] public ButtonPanelTargetSettings Target { get; set; }
        [ProtoMember(5)] public ButtonPanelActionSettings Action { get; set; }

        public ButtonPanelEntrySettings Clone()
        {
            return new ButtonPanelEntrySettings
            {
                Index = Index,
                Title = Title,
                SpriteName = SpriteName,
                Target = Target == null ? null : Target.Clone(),
                Action = Action == null ? null : Action.Clone()
            };
        }

        public bool HasContent()
        {
            return !string.IsNullOrWhiteSpace(Title) ||
                   !string.IsNullOrWhiteSpace(SpriteName) ||
                   Target != null ||
                   Action != null;
        }
    }

    [ProtoContract]
    sealed class ButtonPanelTargetSettings
    {
        [ProtoMember(1)] public int Kind { get; set; }
        [ProtoMember(2)] public string Id { get; set; }
        [ProtoMember(3)] public string DisplayName { get; set; }
        [ProtoMember(4)] public string SpriteName { get; set; }
        [ProtoMember(5)] public string TypeName { get; set; }

        public ButtonPanelTargetSettings Clone()
        {
            return new ButtonPanelTargetSettings
            {
                Kind = Kind,
                Id = Id,
                DisplayName = DisplayName,
                SpriteName = SpriteName,
                TypeName = TypeName
            };
        }

        public string CompatibilityKey
        {
            get
            {
                if (!string.IsNullOrEmpty(TypeName))
                    return Kind + ":" + TypeName;

                return Kind + ":" + (Id ?? string.Empty);
            }
        }

        public PickActionTargetResult ToPickResult()
        {
            return new PickActionTargetResult
            {
                Kind = (PickActionTargetKind)Kind,
                Id = Id,
                DisplayName = DisplayName,
                SpriteName = SpriteName,
                TypeName = TypeName
            };
        }

        public static ButtonPanelTargetSettings FromPickResult(PickActionTargetResult result)
        {
            if (result == null)
                return null;

            return new ButtonPanelTargetSettings
            {
                Kind = (int)result.Kind,
                Id = result.Id,
                DisplayName = result.DisplayName,
                SpriteName = result.SpriteName,
                TypeName = result.TypeName
            };
        }
    }

    [ProtoContract]
    sealed class ButtonPanelActionSettings
    {
        [ProtoMember(1)] public string BaseId { get; set; }
        [ProtoMember(2)] public string DisplayName { get; set; }
        [ProtoMember(3)] public string ActionTypeName { get; set; }
        [ProtoMember(4)] public string SpriteName { get; set; }
        [ProtoMember(5)] public string ParameterTypeName { get; set; }
        [ProtoMember(6)] public string ParameterValue { get; set; }
        [ProtoMember(7)] public string ParameterDisplayValue { get; set; }
        [ProtoMember(8)] public string ClickAction { get; set; }
        [ProtoMember(9)] public string ScrollMode { get; set; }

        public ButtonPanelActionSettings Clone()
        {
            return new ButtonPanelActionSettings
            {
                BaseId = BaseId,
                DisplayName = DisplayName,
                ActionTypeName = ActionTypeName,
                SpriteName = SpriteName,
                ParameterTypeName = ParameterTypeName,
                ParameterValue = ParameterValue,
                ParameterDisplayValue = ParameterDisplayValue,
                ClickAction = ClickAction,
                ScrollMode = ScrollMode
            };
        }

        public void CopyParametersFrom(ButtonPanelActionSettings source)
        {
            if (source == null)
                return;

            ParameterTypeName = source.ParameterTypeName;
            ParameterValue = source.ParameterValue;
            ParameterDisplayValue = source.ParameterDisplayValue;
            ClickAction = source.ClickAction;
            ScrollMode = source.ScrollMode;
        }

        public override string ToString()
        {
            return DisplayName ?? BaseId ?? string.Empty;
        }
    }
}
