using Envoy.Core.Configuration;
using Envoy.Core.Models;
using Xunit;

namespace Envoy.Core.Tests;

public class EnvoySettingsTests
{
    [Fact]
    public void Defaults_AreSafe_StealthOffAndModeSafe()
    {
        var settings = new EnvoySettings();

        // Stealth input emulation must be a deliberate opt-in, never the default.
        Assert.False(settings.StealthModeEnabled);
        Assert.Equal(ExecutionMode.Safe, settings.DefaultMode);
    }
}
