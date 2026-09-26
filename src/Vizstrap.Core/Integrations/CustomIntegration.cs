using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Integrations;

/// <summary>A program started together with Roblox (Bloxstrap's custom integrations).</summary>
public sealed class CustomIntegration : INotifyPropertyChanged
{
    private string _name = "";
    private string _location = "";
    private string _launchArgs = "";
    private bool _autoClose = true;

    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Path of the program (or any file Windows can open).</summary>
    public string Location { get => _location; set => Set(ref _location, value); }

    /// <summary>Command line arguments; line breaks are joined with spaces.</summary>
    public string LaunchArgs { get => _launchArgs; set => Set(ref _launchArgs, value); }

    /// <summary>Close the program when Roblox closes.</summary>
    public bool AutoClose { get => _autoClose; set => Set(ref _autoClose, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CustomIntegration Clone() => new() { Name = Name, Location = Location, LaunchArgs = LaunchArgs, AutoClose = AutoClose };

    public bool SameAs(CustomIntegration other) =>
        Name == other.Name && Location == other.Location && LaunchArgs == other.LaunchArgs && AutoClose == other.AutoClose;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public static class CustomIntegrations
{
    private const string LogSource = nameof(CustomIntegrations);

    /// <summary>Starts every integration; returns the ids of those to close when Roblox closes.</summary>
    public static IReadOnlyList<int> StartAll(IEnumerable<CustomIntegration> integrations)
    {
        var autoClose = new List<int>();

        foreach (var integration in integrations.Where(integration => !string.IsNullOrWhiteSpace(integration.Location)))
        {
            try
            {
                using var process = Process.Start(StartInfoFor(integration));
                Log.Info(LogSource, $"Started '{integration.Name}' ({integration.Location}, PID {process?.Id})");

                if (integration.AutoClose && process is not null)
                    autoClose.Add(process.Id);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                Log.Warn(LogSource, $"Couldn't start '{integration.Name}' ({integration.Location}): {ex.Message}");
            }
        }

        return autoClose;
    }

    /// <summary>Asks the programs to close, like closing their window (Bloxstrap does the same).</summary>
    public static void CloseAll(IEnumerable<int> processIds)
    {
        foreach (int processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);

                if (!process.HasExited)
                {
                    Log.Info(LogSource, $"Closing {process.ProcessName} (PID {processId})");
                    process.CloseMainWindow();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // already gone
            }
        }
    }

    internal static ProcessStartInfo StartInfoFor(CustomIntegration integration) => new(integration.Location)
    {
        Arguments = integration.LaunchArgs.Replace("\r\n", " ").Replace('\n', ' '),
        WorkingDirectory = Path.GetDirectoryName(integration.Location) ?? "",
        UseShellExecute = true,
    };
}
