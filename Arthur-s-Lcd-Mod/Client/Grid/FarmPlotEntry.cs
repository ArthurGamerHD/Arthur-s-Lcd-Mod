using Sandbox.ModAPI;
using IMyFarmPlotLogic = Sandbox.ModAPI.IMyFarmPlotLogic;

namespace LcdMod.Client.Grid
{
    public sealed class FarmPlotEntry
    {
        public FarmPlotEntry(IMyFunctionalBlock block, IMyFarmPlotLogic logic,
            IMyResourceStorageComponent storageComponent)
        {
            Block = block;
            Logic = logic;
            StorageComponent = storageComponent;
        }

        public IMyFunctionalBlock Block { get; private set; }
        public IMyFarmPlotLogic Logic { get; private set; }
        public  IMyResourceStorageComponent StorageComponent { get; private set; }
    }
}
