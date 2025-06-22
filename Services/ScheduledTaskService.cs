using System.Diagnostics;

namespace BrowserReporterService.Services
{
    public class ScheduledTaskService
    {
        private const string TaskName = "BrowserReporter";

        public void Install()
        {
            string exePath = Process.GetCurrentProcess().MainModule!.FileName;
            string arguments = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F";
            
            ExecuteSchtasks(arguments, "install");
        }

        public void Uninstall()
        {
            string arguments = $"/Delete /TN \"{TaskName}\" /F";
            ExecuteSchtasks(arguments, "uninstall");
        }

        private void ExecuteSchtasks(string arguments, string action)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine($"Failed to start schtasks.exe to {action} the task.");
                return;
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"Scheduled task '{TaskName}' successfully {action}ed.");
                Console.WriteLine(output);
            }
            else
            {
                Console.WriteLine($"Error {action}ing scheduled task. Exit code: {process.ExitCode}");
                Console.WriteLine("Output:");
                Console.WriteLine(output);
                Console.WriteLine("Error:");
                Console.WriteLine(error);
            }
        }
    }
} 