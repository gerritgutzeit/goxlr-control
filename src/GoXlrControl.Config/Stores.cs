using System.Text.Json;

namespace GoXlrControl.Config;

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GoXlrControlStudio");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "settings.json");
    }

    public string RootDirectory => Path.GetDirectoryName(_path)!;

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(_path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonConfig.Options) ?? new AppSettings();
        var before = settings.SchemaVersion;
        SchemaMigrator.MigrateSettings(settings);
        if (settings.SchemaVersion != before)
            Save(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.SchemaVersion = SchemaMigrator.CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(settings, JsonConfig.Options);
        File.WriteAllText(_path, json);
    }
}

public sealed class ProfileStore
{
    private readonly string _directory;

    public ProfileStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GoXlrControlStudio");
        _directory = Path.Combine(root, "profiles");
        Directory.CreateDirectory(_directory);
    }

    public IReadOnlyList<ControllerProfile> LoadAll()
    {
        var list = new List<ControllerProfile>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var profile = LoadFile(file);
                if (profile is not null)
                    list.Add(profile);
            }
            catch
            {
                // corrupt file ignored; diagnostics can report separately
            }
        }

        if (list.Count == 0)
        {
            var created = ControllerProfile.CreateDefault();
            Save(created);
            list.Add(created);
        }

        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ControllerProfile? LoadById(string id)
    {
        var path = Path.Combine(_directory, $"{id}.json");
        return File.Exists(path) ? LoadFile(path) : null;
    }

    public void Save(ControllerProfile profile)
    {
        SchemaMigrator.MigrateProfile(profile);
        profile.SchemaVersion = SchemaMigrator.CurrentSchemaVersion;
        var path = Path.Combine(_directory, $"{profile.Id}.json");
        var json = JsonSerializer.Serialize(profile, JsonConfig.Options);
        File.WriteAllText(path, json);
    }

    public void Delete(string id)
    {
        var path = Path.Combine(_directory, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);
    }

    public ControllerProfile Duplicate(ControllerProfile source, string newName)
    {
        var copy = JsonSerializer.Deserialize<ControllerProfile>(
            JsonSerializer.Serialize(source, JsonConfig.Options), JsonConfig.Options)!;
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = newName;
        Save(copy);
        return copy;
    }

    public void Export(ControllerProfile profile, string destinationPath)
    {
        var json = JsonSerializer.Serialize(profile, JsonConfig.Options);
        File.WriteAllText(destinationPath, json);
    }

    public ControllerProfile Import(string sourcePath)
    {
        var json = File.ReadAllText(sourcePath);
        var profile = JsonSerializer.Deserialize<ControllerProfile>(json, JsonConfig.Options)
                      ?? throw new InvalidDataException("Ungültiges Profil.");
        SchemaMigrator.MigrateProfile(profile);
        profile.Id = Guid.NewGuid().ToString("N");
        Save(profile);
        return profile;
    }

    private static ControllerProfile? LoadFile(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<ControllerProfile>(json, JsonConfig.Options);
        if (profile is null) return null;
        var before = profile.SchemaVersion;
        SchemaMigrator.MigrateProfile(profile);
        if (profile.SchemaVersion != before)
        {
            profile.SchemaVersion = SchemaMigrator.CurrentSchemaVersion;
            File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonConfig.Options));
        }

        return profile;
    }
}

public static class SchemaMigrator
{
    public const int CurrentSchemaVersion = 2;

    public static void MigrateSettings(AppSettings settings)
    {
        if (settings.SchemaVersion < 1)
            settings.SchemaVersion = 1;
        if (settings.SchemaVersion < 2)
        {
            if (string.IsNullOrWhiteSpace(settings.DiscordMuteChord))
                settings.DiscordMuteChord = "Ctrl+Shift+M";
            if (string.IsNullOrWhiteSpace(settings.DiscordDeafenChord))
                settings.DiscordDeafenChord = "Ctrl+Shift+D";
            settings.SchemaVersion = 2;
        }
    }

    public static void MigrateProfile(ControllerProfile profile)
    {
        if (profile.SchemaVersion < 1)
            profile.SchemaVersion = 1;

        if (profile.Faders.Count == 0)
            profile.Faders = ControllerProfile.CreateDefaultFaders();

        if (profile.Buttons.Count == 0)
            profile.Buttons = ControllerProfile.CreateDefaultButtons();

        if (profile.SchemaVersion < 2)
        {
            foreach (var fader in profile.Faders)
            {
                if (fader.Target.Kind == FaderTargetKind.MasterVolume)
                    fader.SyncMode = SyncMode.Absolute;
            }

            profile.SchemaVersion = 2;
        }
    }
}
