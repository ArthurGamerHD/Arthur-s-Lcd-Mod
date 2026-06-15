using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigMarkdown : ScreenConfigInteractive
    {
        public override int Id => 17;

        [ProtoMember(24)]
        public string RawText { get; set; } =
            @"# This is a Title

This is a paragraph with **bold**, *italic*, and [color:#00FF00]colored text[/color].

---

## This is a List

1. This is the first item
2. This item uses [font:""monospace""]monospace text[/font]
3. This item uses [color:#FF0000][font:""monospace""]red monospace text[/font][/color]

---

![Connector](sprite:MyObjectBuilder_ShipConnector/LargeBlockInsetConnector) ![Arrow](sprite:Arrow) ![Danger](sprite:Danger) Images from Sprites.

---

###### This is a Small Heading

Click [color:#0000FF]""[loc]BlockPropertyTitle_TextPanelShowTextPanel[/loc]""[/color] to edit this text";
    }
}
