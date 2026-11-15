using System.Windows;
using Nimbus.AceV4;
using Nimbus.Services;
using Nimbus.Services.Ace;

namespace Nimbus
{
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            AceRuntime.Current.Dispose();   // saves Ace V4's state and stops the mic, voice and sounds
            Hub.Shutdown();
            AceBrain.ShutdownShared();   // stops the LFM3-Omni server Nimbus started, so it doesn't keep holding VRAM
            base.OnExit(e);
        }
    }
}
