namespace LcdMod.Client.Animation
{
    public sealed class AnimationHandle
    {
        AnimationController _controller;

        internal AnimationHandle(AnimationController controller)
        {
            _controller = controller;
            State = AnimationState.Running;
        }

        public AnimationState State { get; internal set; }

        public bool IsRunning => State == AnimationState.Running;

        public bool IsCompleted => State == AnimationState.Completed;

        public bool IsCancelled => State == AnimationState.Cancelled;

        public void Cancel()
        {
            AnimationController controller = _controller;
            if (controller != null)
                controller.Cancel(this);
        }

        /// <summary>
        /// Immediately applies the end of the current and all remaining steps.
        /// </summary>
        public void Complete()
        {
            AnimationController controller = _controller;
            if (controller != null)
                controller.Complete(this);
        }

        internal void Detach(AnimationState state)
        {
            State = state;
            _controller = null;
        }
    }
}
