using FluentAssertions;
using GoXlrControl.Hardware;

namespace GoXlrControl.Hardware.Tests;

public class UtilityPatcherTests
{
    [Fact]
    public void SelectWindowsInstallerUrl_PrefersGoxlrUtilityExe()
    {
        const string json = """
        {
          "assets": [
            { "name": "goxlr-utility-1.2.4-1.x86_64.rpm", "browser_download_url": "https://example/a.rpm" },
            { "name": "goxlr-utility-macos-1.2.4-m1.pkg", "browser_download_url": "https://example/m.pkg" },
            { "name": "other-tool.exe", "browser_download_url": "https://example/other.exe" },
            { "name": "goxlr-utility-1.2.4.exe", "browser_download_url": "https://example/goxlr-utility-1.2.4.exe" }
          ]
        }
        """;

        UtilityPatcher.SelectWindowsInstallerUrl(json)
            .Should().Be("https://example/goxlr-utility-1.2.4.exe");
    }

    [Fact]
    public void SelectWindowsInstallerUrl_IgnoresMacOsExeNamedAssets()
    {
        const string json = """
        {
          "assets": [
            { "name": "helper-macos.exe", "browser_download_url": "https://example/bad.exe" },
            { "name": "setup.exe", "browser_download_url": "https://example/setup.exe" }
          ]
        }
        """;

        UtilityPatcher.SelectWindowsInstallerUrl(json)
            .Should().Be("https://example/setup.exe");
    }

    [Fact]
    public void CandidateDaemonPaths_IncludeProgramFiles()
    {
        UtilityDaemonLifecycle.CandidateDaemonPaths()
            .Should().Contain(p => p.Contains("GoXLR Utility", StringComparison.OrdinalIgnoreCase)
                                   && p.Contains("goxlr-daemon", StringComparison.OrdinalIgnoreCase));
    }
}
