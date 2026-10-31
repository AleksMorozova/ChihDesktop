using Microsoft.Win32;
using System.IO;

namespace ChihDesktop;

public sealed class WindowsAutostartRegistration : IAutostartRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("ChihDesktop") as string; }
    public void Write(string? command)
    {
        if (command is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(Key, true);
            existing?.DeleteValue("ChihDesktop", false); return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(Key, true) ?? throw new IOException("Нет доступа к автозапуску текущего пользователя.");
        key.SetValue("ChihDesktop", command, RegistryValueKind.String);
    }
    public string Command(bool petOnly)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("Путь приложения недоступен.");
        if (!string.Equals(Path.GetFileName(executable), "ChihDesktop.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Автозапуск включается из установленного ChihDesktop.exe.");
        return "\"" + executable + "\"" + (petOnly ? " --pet-only" : "");
    }
}
