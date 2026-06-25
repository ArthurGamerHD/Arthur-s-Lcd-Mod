using Generated;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;

namespace LcdMod.Client.Apps.Abstract
{
    [ConfigComponent(LcdMod.Common.Helpers.Constants.GENERAL, typeof(GeneralConfigComponent), PropertyName = "GeneralComponent")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.COLORS, typeof(ColorConfigComponent), PropertyName = "ColorComponent")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.INTERACTION, typeof(InteractiveConfigComponent), PropertyName = "InteractionComponent")]
    public abstract partial class App
    {
        protected App(IComponentContainer config) { Config = config; }
        protected IComponentContainer Config { get; }
    }

    [ConfigComponent(LcdMod.Common.Helpers.Constants.FILTERS, typeof(FilterConfigComponent), PropertyName = "FilterComponent")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.BLOCKS, typeof(BlockSelectionConfigComponent), PropertyName = "BlockSelectionComponent")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.ITEMS, typeof(ItemSelectionConfigComponent), PropertyName = "ItemSelectionComponent")]
    public abstract partial class ItemsApp : App
    {
        protected ItemsApp(IComponentContainer config) : base(config) { }
    }
}

namespace LcdMod.Client.Apps
{
    using LcdMod.Client.Apps.Abstract;

    [LcdApp(1, Name = "Power")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.APP, typeof(PowerConfigComponent), PropertyName = "PowerComponent")]
    public sealed partial class GeneratedPowerApp : App
    {
        public GeneratedPowerApp(IComponentContainer config) : base(config) { }
        public PowerConfigComponent ReadPower() => PowerComponent;
        public GeneralConfigComponent ReadGeneral() => GeneralComponent;
    }

    [LcdApp(2, Name = "Projector")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "ProjectorReferenceComponent")]
    public sealed partial class GeneratedProjectorApp : ItemsApp
    {
        public GeneratedProjectorApp(IComponentContainer config) : base(config) { }
        public BlockReferenceConfigComponent ReadReference() => ProjectorReferenceComponent;
        public ItemSelectionConfigComponent ReadItems() => ItemSelectionComponent;
    }

    [LcdApp(3, Name = "Farm")]
    public sealed partial class GeneratedFarmApp : App
    {
        public GeneratedFarmApp(IComponentContainer config) : base(config) { }
    }

    [LcdApp(4, Name = "Radar")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.APP, typeof(RadarConfigComponent), PropertyName = "RadarComponent")]
    public sealed partial class GeneratedRadarApp : App
    {
        public GeneratedRadarApp(IComponentContainer config) : base(config) { }
        public RadarConfigComponent ReadRadar() => RadarComponent;
    }

    [LcdApp(5, Name = "Markdown")]
    [ConfigComponent(LcdMod.Common.Helpers.Constants.APP, typeof(MarkdownConfigComponent), PropertyName = "MarkdownComponent")]
    public sealed partial class GeneratedMarkdownApp : App
    {
        public GeneratedMarkdownApp(IComponentContainer config) : base(config) { }
    }

    [LcdApp(6, Name = "Transfer")]
    [ConfigComponent("referenceblock.slot", typeof(BlockSelectionConfigComponent), PropertyName = "ReferenceBlocksComponent")]
    [ConfigComponent("otherblock.slot", typeof(BlockSelectionConfigComponent), PropertyName = "OtherBlocksComponent")]
    public sealed partial class GeneratedTransferApp : App
    {
        public GeneratedTransferApp(IComponentContainer config) : base(config) { }
        public BlockSelectionConfigComponent ReadReferenceBlocks() => ReferenceBlocksComponent;
        public BlockSelectionConfigComponent ReadOtherBlocks() => OtherBlocksComponent;
    }
}

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public abstract partial class SurfaceScriptBase
    {
        protected abstract AppType AppType { get; }
    }
}

namespace LcdMod.Client.SurfaceScripts
{
    using LcdMod.Client.Apps;
    using LcdMod.Client.SurfaceScripts.Abstract;

    [LcdSurface(typeof(GeneratedPowerApp))]
    public sealed partial class GeneratedPowerSurface : SurfaceScriptBase
    {
        readonly GeneratedPowerApp _app;
        public GeneratedPowerSurface()
        {
            Config = AppSchemaRegistry.CreateSurface(AppType.Power, 0);
            _app = new GeneratedPowerApp(Config);
        }
        public SurfaceConfig Config { get; }
        public AppType DeclaredAppType => AppType;
        public GeneratedPowerApp App => _app;
    }
}

namespace Arthur_s_Lcd_Mod.Tests
{
    using System.Reflection;
    using LcdMod.Client.Apps;
    using LcdMod.Client.SurfaceScripts;

    public sealed class GeneratedComponentPropertyTests
    {
        [Fact]
        public void Generator_CreatesDirectFailFastProperties()
        {
            var config = AppSchemaRegistry.CreateSurface(AppType.Power, 0);
            var app = new GeneratedPowerApp(config);

            Assert.Same(config.Get<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP), app.ReadPower());
            Assert.Same(config.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL), app.ReadGeneral());
        }

        [Fact]
        public void Generator_AggregatesBaseComponentsAndUsesSemanticSlots()
        {
            var config = AppSchemaRegistry.CreateSurface(AppType.Projector, 0);
            var app = new GeneratedProjectorApp(config);

            Assert.Same(config.Get<BlockReferenceConfigComponent>(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE), app.ReadReference());
            Assert.Same(config.Get<ItemSelectionConfigComponent>(LcdMod.Common.Helpers.Constants.ITEMS), app.ReadItems());
        }

        [Fact]
        public void FarmContract_DoesNotGeneratePowerProperty()
        {
            var property = typeof(GeneratedFarmApp).GetProperty(
                "PowerComponent",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.Null(property);
            var config = AppSchemaRegistry.CreateSurface(AppType.Farm, 0);
            Assert.Null(config.TryGet<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP));
        }


        [Fact]
        public void Generator_SupportsRepeatedComponentTypesInDistinctSlots()
        {
            var config = AppSchemaRegistry.CreateSurface(AppType.Transfer, 0);
            var app = new GeneratedTransferApp(config);

            Assert.Same(config.Get<BlockSelectionConfigComponent>("referenceblock.slot"), app.ReadReferenceBlocks());
            Assert.Same(config.Get<BlockSelectionConfigComponent>("otherblock.slot"), app.ReadOtherBlocks());
            Assert.NotSame(app.ReadReferenceBlocks(), app.ReadOtherBlocks());
        }

        [Fact]
        public void SurfaceMapping_IsGeneratedFromAppClass()
        {
            var surface = new GeneratedPowerSurface();

            Assert.Equal(AppType.Power, surface.DeclaredAppType);
            Assert.Equal((int)AppType.Power, surface.Config.AppTypeId);
        }
    }
}
