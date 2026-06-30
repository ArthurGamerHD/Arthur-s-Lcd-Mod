namespace LcdMod.Client.Animation
{
    public enum AnimationConflict
    {
        /// <summary>
        /// Permit multiple animations on the same owner and channel.
        /// </summary>
        Allow,

        /// <summary>
        /// Cancel an existing animation on the same owner and channel.
        /// </summary>
        Replace,

        /// <summary>
        /// Keep the existing animation and return its handle.
        /// </summary>
        Ignore
    }
}
