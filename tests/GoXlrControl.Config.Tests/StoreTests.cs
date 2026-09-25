using FluentAssertions;
using GoXlrControl.Config;

namespace GoXlrControl.Config.Tests;

public class ProfileStoreTests
{
    [Fact]
    public void SaveLoadRoundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "goxlr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProfileStore(root);
            var profile = ControllerProfile.CreateDefault("Test");
            profile.Faders[0].Label = "MasterX";
            store.Save(profile);

            var loaded = store.LoadById(profile.Id);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Test");
            loaded.Faders[0].Label.Should().Be("MasterX");

            var copy = store.Duplicate(loaded, "Copy");
            copy.Id.Should().NotBe(loaded.Id);
            copy.Name.Should().Be("Copy");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadAll_MovesCorruptProfileToBak()
    {
        var root = Path.Combine(Path.GetTempPath(), "goxlr-test-" + Guid.NewGuid().ToString("N"));
        var profilesDir = Path.Combine(root, "profiles");
        Directory.CreateDirectory(profilesDir);
        try
        {
            var corruptPath = Path.Combine(profilesDir, "bad.json");
            File.WriteAllText(corruptPath, "{ not-valid-json");

            var store = new ProfileStore(root);
            var loaded = store.LoadAll();
            loaded.Should().NotBeEmpty();
            File.Exists(corruptPath).Should().BeFalse();
            File.Exists(corruptPath + ".bak").Should().BeTrue();
            File.ReadAllText(corruptPath + ".bak").Should().Contain("not-valid-json");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SettingsStore_Defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "goxlr-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsStore(root);
            var s = store.Load();
            s.CloseToTray.Should().BeTrue();
            s.SyncModeDefault.Should().Be(SyncMode.SoftTakeover);
            s.SchemaVersion.Should().Be(2);
            s.DiscordMuteChord.Should().Be("Ctrl+Shift+M");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
