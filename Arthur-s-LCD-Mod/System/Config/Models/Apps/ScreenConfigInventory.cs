using System;
using System.Linq;
using Graph.Helpers;
using ProtoBuf;
using VRage.Game;

namespace Graph.System.Config.Models.Apps
{
    [ProtoContract]
    [ProtoInclude(109, typeof(ScreenConfigProjector))]
    public partial class ScreenConfigWithItems : ScreenConfigWithBlocks
    {
        public override int Id => 12;

        [ProtoMember(5)] public string[] SelectedDefinition { get; set; } = Array.Empty<string>();
        [ProtoMember(6)] public string[] SelectedCategories { get; set; } = Array.Empty<string>();

        public MyDefinitionId[] SelectedItems
        {
            get
            {
                try
                {
                    return SelectedDefinition.Select(MyDefinitionId.Parse).ToArray();
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, this);
                }

                return Array.Empty<MyDefinitionId>();
            }
            set { SelectedDefinition = value.Select(a => a.ToString()).ToArray(); }
        }

    }
}
