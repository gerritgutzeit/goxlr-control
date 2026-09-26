using System.IO;
using Microsoft.Win32;
using Velopack.Windows;

namespace GoXlrControl.App.Services;

/// <summary>
/// Registers the app to start at Windows login.
/// A Startup-folder shortcut sets the working directory. The Run key does not,
/// so Windows would launch the process with System32 as the current directory.
/// </summary>
internal static class WindowsAutostart
{
    public const string RunValueName = "GoXlrControlStudio";
    public const string ShortcutFileName = "GoXLR Control Studio.lnk";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedFolderPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string StartupApprovedRunPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static (bool Ok, string Message) Apply(bool enabled, bool userInitiated)
    {
        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            ShortcutFileName);

        if (!enabled)
        {
            RemoveRunValue();
            TryDelete(shortcutPath);
            return (true, "Mit Windows starten ist aus.");
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return (false, "Autostart nicht gesetzt: Programmdatei wurde nicht gefunden.");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            WriteShortcut(shortcutPath, exe);
            RemoveRunValue();
            if (userInitiated)
                EnableStartupApproval(StartupApprovedFolderPath, ShortcutFileName);
            return (true, "Mit Windows starten ist an. Die App startet beim nächsten Anmelden.");
        }
        catch (Exception ex)
        {
            try
            {
                WriteRunValue(exe!);
                if (userInitiated)
                    EnableStartupApproval(StartupApprovedRunPath, RunValueName);
                return (true, "Mit Windows starten ist an. Die App startet beim nächsten Anmelden.");
            }
            catch (Exception regEx)
            {
                return (false, $"Autostart fehlgeschlagen: {ex.Message} / {regEx.Message}");
            }
        }
    }

    private static void WriteShortcut(string shortcutPath, string exe)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var link = new ShellLink
                {
                    Target = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                    Description = "GoXLR Control Studio",
                    IconPath = exe,
                    IconIndex = 0
                };
                link.Save(shortcutPath);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }

    private static void WriteRunValue(string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Run-Schlüssel konnte nicht geöffnet werden.");
        key.SetValue(RunValueName, $"\"{exe}\"");
    }

    private static void RemoveRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(RunValueName) is not null)
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private static void EnableStartupApproval(string keyPath, string valueName)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        // 0x02 = enabled. Windows leaves new entries disabled after a failed login start.
        key?.SetValue(valueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
