using Generated;

namespace LcdMod.Client.Terminal.Controls.Proxy
{
    public interface IProxyAutoOffset : IUsesTerminalControl<ButtonProxyAuto>
    {
        bool CanApplyProxyAutoOffset();
        void ApplyProxyAutoOffset();
    }
}
