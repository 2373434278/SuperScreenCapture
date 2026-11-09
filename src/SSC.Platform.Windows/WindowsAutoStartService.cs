using System.Security;
using Microsoft.Win32;
using SSC.Application.Abstractions;

namespace SSC.Platform.Windows;

public sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SuperScreenCapture";

    public AutoStartStatus GetStatus()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return string.IsNullOrWhiteSpace(value)
                ? new AutoStartStatus(AutoStartState.Disabled)
                : new AutoStartStatus(AutoStartState.Enabled, value);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return new AutoStartStatus(AutoStartState.Unavailable, exception.Message);
        }
    }

    public AutoStartChangeResult SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return new AutoStartChangeResult(false, AutoStartState.Unavailable, "The Run registry key is unavailable.");
            }

            if (enabled)
            {
                var command = BuildCommand();
                if (command is null)
                {
                    return new AutoStartChangeResult(
                        false,
                        AutoStartState.Unavailable,
                        "The application path could not be resolved.");
                }

                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return new AutoStartChangeResult(true, enabled ? AutoStartState.Enabled : AutoStartState.Disabled);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return new AutoStartChangeResult(false, GetStatus().State, exception.Message);
        }
    }

    /// <summary>
    /// Velopack 安装位于版本化目录，升级后旧路径会失效；
    /// 应用每次启动且期望开启自启时应该重新写一次。
    /// </summary>
    public static void EnsureRegistered()
    {
        var command = BuildCommand();
        if (command is null)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Best-effort refresh; the settings toggle surfaces real failures.
        }
    }

    private static string? BuildCommand()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return $"\"{path}\" --hidden";
    }
}
