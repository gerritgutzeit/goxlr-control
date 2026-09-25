using FluentAssertions;
using GoXlrControl.Config;
using GoXlrControl.Engine;
using GoXlrControl.Integrations;

namespace GoXlrControl.Integrations.Tests;

public class FallbackDiscordIntegrationTests
{
    [Fact]
    public async Task Start_LeavesMuteAndDeafenUnknown()
    {
        var sut = CreateSut(out _);
        await sut.StartAsync();

        sut.Current.Mute.Should().Be(DiscordTriState.Unknown);
        sut.Current.Deafen.Should().Be(DiscordTriState.Unknown);
        sut.Current.Reliability.Should().Be(DiscordStatusReliability.Unknown);
        sut.Mode.Should().Be(DiscordOperatingMode.Fallback);
    }

    [Fact]
    public async Task ToggleMute_SendsChord_AndReportsMirroredNotConfirmed()
    {
        var sent = new List<string>();
        var sut = CreateSut(out _, chord => sent.Add(chord), mute: "Ctrl+Shift+M");
        await sut.StartAsync();

        DiscordVoiceSnapshot? last = null;
        sut.StateChanged += (_, snap) => last = snap;

        await sut.ToggleMuteAsync();

        sent.Should().Equal("Ctrl+Shift+M");
        last.Should().NotBeNull();
        last!.Reliability.Should().Be(DiscordStatusReliability.Mirrored);
        last.Mute.Should().Be(DiscordTriState.On);
        last.Deafen.Should().Be(DiscordTriState.Unknown);
        last.Connection.Should().Be(DiscordConnectionState.Disconnected);
        last.StatusConfirmedAt.Should().NotBeNull();
        sut.Current.Reliability.Should().Be(DiscordStatusReliability.Mirrored);
        sut.Current.Reliability.Should().NotBe(DiscordStatusReliability.Confirmed);
    }

    [Fact]
    public async Task Start_ReportsDisconnectedVoiceApiConnection()
    {
        var sut = CreateSut(out _);
        await sut.StartAsync();
        sut.Current.Connection.Should().Be(DiscordConnectionState.Disconnected);
        sut.ConnectionState.Should().Be(DiscordConnectionState.Disconnected);
    }

    [Fact]
    public async Task ToggleMute_Twice_FlipsMirrorOff()
    {
        var sut = CreateSut(out _);
        await sut.StartAsync();
        await sut.ToggleMuteAsync();
        await sut.ToggleMuteAsync();
        sut.Current.Mute.Should().Be(DiscordTriState.Off);
        sut.Current.Reliability.Should().Be(DiscordStatusReliability.Mirrored);
    }

    [Fact]
    public async Task SyncMirroredMute_SetsStateWithoutSendingChord()
    {
        var sut = CreateSut(out var sent);
        await sut.StartAsync();
        sut.SyncMirroredMute(true);
        sent.Should().BeEmpty();
        sut.Current.Mute.Should().Be(DiscordTriState.On);
        sut.Current.Reliability.Should().Be(DiscordStatusReliability.Mirrored);
    }

    [Fact]
    public async Task ToggleMute_WithoutChord_Throws()
    {
        var sut = CreateSut(out _, mute: null);
        await sut.StartAsync();
        var act = async () => await sut.ToggleMuteAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ToggleMute_BeforeStart_Throws()
    {
        var sut = CreateSut(out _, mute: "Ctrl+M");
        var act = async () => await sut.ToggleMuteAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static FallbackDiscordIntegration CreateSut(
        out List<string> sent,
        Action<string>? onSend = null,
        string? mute = "Ctrl+Shift+M",
        string? deafen = "Ctrl+Shift+D")
    {
        sent = new List<string>();
        var capture = sent;
        return new FallbackDiscordIntegration(
            chord =>
            {
                capture.Add(chord);
                onSend?.Invoke(chord);
            },
            () => mute,
            () => deafen);
    }
}

public class DiscordIntegrationFactoryTests
{
    [Fact]
    public void ResolveEffectiveMode_Fallback_StaysFallback()
    {
        var mode = DiscordIntegrationFactory.ResolveEffectiveMode(
            DiscordIntegrationMode.Fallback, out var reason);
        mode.Should().Be(DiscordIntegrationMode.Fallback);
        reason.Should().BeNull();
    }

    [Fact]
    public void ResolveEffectiveMode_HybridWithoutGate_DowngradesToFallback()
    {
        var previous = DiscordAuthorizationGate.AuthorizedApisEnabled;
        try
        {
            DiscordAuthorizationGate.AuthorizedApisEnabled = false;
            var mode = DiscordIntegrationFactory.ResolveEffectiveMode(
                DiscordIntegrationMode.Hybrid, out var reason);
            mode.Should().Be(DiscordIntegrationMode.Fallback);
            reason.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            DiscordAuthorizationGate.AuthorizedApisEnabled = previous;
        }
    }

    [Fact]
    public void ResolveEffectiveMode_NativeWithGateButNoBackend_StillFallback()
    {
        var previous = DiscordAuthorizationGate.AuthorizedApisEnabled;
        try
        {
            DiscordAuthorizationGate.AuthorizedApisEnabled = true;
            var mode = DiscordIntegrationFactory.ResolveEffectiveMode(
                DiscordIntegrationMode.Native, out var reason);
            mode.Should().Be(DiscordIntegrationMode.Fallback);
            reason.Should().Contain("nicht implementiert");
        }
        finally
        {
            DiscordAuthorizationGate.AuthorizedApisEnabled = previous;
        }
    }

    [Fact]
    public async Task Create_AlwaysReturnsFallbackImplementation()
    {
        var previous = DiscordAuthorizationGate.AuthorizedApisEnabled;
        try
        {
            DiscordAuthorizationGate.AuthorizedApisEnabled = false;
            var settings = new AppSettings
            {
                DiscordIntegrationMode = DiscordIntegrationMode.Native,
                DiscordMuteChord = "Ctrl+M"
            };
            var shortcuts = new SendInputShortcutService();
            var integration = DiscordIntegrationFactory.Create(() => settings, shortcuts);
            try
            {
                integration.Should().BeOfType<FallbackDiscordIntegration>();
            }
            finally
            {
                await integration.DisposeAsync();
            }
        }
        finally
        {
            DiscordAuthorizationGate.AuthorizedApisEnabled = previous;
        }
    }
}

public class SendInputShortcutServiceTests
{
    [Theory]
    [InlineData("Ctrl+Shift+M", 0x11, 0x10, 0x4D)]
    [InlineData("ctrl+shift+m", 0x11, 0x10, 0x4D)]
    [InlineData("Alt+F4", 0x12, 0x73)]
    public void ParseChord_ResolvesModifiersAndKeys(string chord, params int[] expected)
    {
        var keys = SendInputShortcutService.ParseChord(chord);
        keys.Select(k => (int)k).Should().Equal(expected);
    }

    [Fact]
    public void ParseChord_RejectsUnknown()
    {
        var act = () => SendInputShortcutService.ParseChord("Ctrl+Foo");
        act.Should().Throw<ArgumentException>();
    }
}
