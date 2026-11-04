using Adk.Mvvm;

namespace LcdMod.Client.Apps.SessionDebug
{
    internal sealed class SessionDebugViewModel : ObservableObject
    {
        public SessionDebugViewModel(SessionDebugApp app)
        {
            App = app;
        }

        public SessionDebugApp App { get; private set; }
    }
}
