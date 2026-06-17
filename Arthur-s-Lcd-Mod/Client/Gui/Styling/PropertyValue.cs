namespace LcdMod.Client.Gui.Styling
{
    public sealed class PropertyValue<TValue>
    {
        public TValue Cache;
        public bool HasCache;
        public TValue Local;
        public bool LocalOverride;

        public void ClearLocal()
        {
            Local = default(TValue);
            LocalOverride = false;
            HasCache = false;
        }
    }
}
