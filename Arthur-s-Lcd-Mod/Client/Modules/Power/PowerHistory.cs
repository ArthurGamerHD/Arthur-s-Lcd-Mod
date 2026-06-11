namespace LcdMod.Client.Modules.Power
{
    public sealed class PowerHistory
    {
        public PowerHistory()
        {
            RawSamples = new RingBuffer<PowerSnapshot>(10);
            Average1Second = new RingBuffer<PowerSnapshot>(10);
            Average5Seconds = new RingBuffer<PowerSnapshot>(10);
            Average30Seconds = new RingBuffer<PowerSnapshot>(10);
            Average1Minute = new RingBuffer<PowerSnapshot>(10);
            Average5Minutes = new RingBuffer<PowerSnapshot>(10);
            Average30Minutes = new RingBuffer<PowerSnapshot>(10);
        }

        public RingBuffer<PowerSnapshot> RawSamples { get; private set; }
        public RingBuffer<PowerSnapshot> Average1Second { get; private set; }
        public RingBuffer<PowerSnapshot> Average5Seconds { get; private set; }
        public RingBuffer<PowerSnapshot> Average30Seconds { get; private set; }
        public RingBuffer<PowerSnapshot> Average1Minute { get; private set; }
        public RingBuffer<PowerSnapshot> Average5Minutes { get; private set; }
        public RingBuffer<PowerSnapshot> Average30Minutes { get; private set; }
    }

    public enum PowerHistoryTier
    {
        Average1Second = 0,
        Average5Seconds = 1,
        Average30Seconds = 2,
        Average1Minute = 3,
        Average5Minutes = 4,
        Average30Minutes = 5
    }
}
