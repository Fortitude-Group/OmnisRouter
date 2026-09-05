using System.Diagnostics;
using System.Security;

namespace OmnisRouter.Tray;

/// <summary>
/// Registers a per-user "at log on" Scheduled Task that launches the tray with restart-on-failure,
/// so it starts on every login and comes back if it crashes (FR-002) without a session-0 service.
/// Uses <c>schtasks.exe</c> with a task XML; no administrator rights are needed for the current
/// user's own task. The installer registers the same task; this lets the Settings window toggle it.
/// </summary>
internal static class LoginTask
{
    public const string TaskName = "OmnisRouter Collect";

    public static bool IsRegistered() => Run("/query", "/tn", TaskName) == 0;

    public static bool Register(string exePath)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>OmnisRouter subscription collect watcher.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>3</Count>
                </RestartOnFailure>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exePath)}</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        var temp = Path.Combine(Path.GetTempPath(), $"omnisrouter-task-{Guid.NewGuid():n}.xml");
        try
        {
            // Task Scheduler expects UTF-16 for this schema.
            File.WriteAllText(temp, xml, System.Text.Encoding.Unicode);
            return Run("/create", "/tn", TaskName, "/xml", temp, "/f") == 0;
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    public static bool Unregister() => Run("/delete", "/tn", TaskName, "/f") == 0;

    private static int Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi);
            if (p is null)
            {
                return -1;
            }

            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return -1;
        }
    }
}
