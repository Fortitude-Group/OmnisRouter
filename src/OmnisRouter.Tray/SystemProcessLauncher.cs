using System.Diagnostics;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Real <see cref="IProcessLauncher"/> for the bundled router: launches it as a hidden child process
/// with no console window (FR-001), per the launch contract (contracts/router-management.md).
/// </summary>
internal sealed class SystemProcessLauncher : IProcessLauncher
{
    public IProcessHandle Start(string exePath, string workingDir, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var entry in environment)
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }

        // The Process object is kept alive by the handle for as long as the caller holds it.
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var handle = new SystemProcessHandle(process);
        process.Exited += (_, _) => handle.RaiseExited();
        process.Start();
        return handle;
    }
}

/// <summary>Wraps a real <see cref="Process"/> as an <see cref="IProcessHandle"/>.</summary>
internal sealed class SystemProcessHandle : IProcessHandle
{
    private readonly Process _process;

    public SystemProcessHandle(Process process) => _process = process;

    public bool HasExited => _process.HasExited;

    public event EventHandler Exited = delegate { };

    public void Kill() => _process.Kill(entireProcessTree: true);

    internal void RaiseExited() => Exited(this, EventArgs.Empty);
}
