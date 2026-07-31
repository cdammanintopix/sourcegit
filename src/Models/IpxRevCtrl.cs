using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Integration with the commands provided by the `ipx-rev-ctrl` package.
    /// </summary>
    public static class IpxRevCtrl
    {
        public static string GetDependenciesExecutable
        {
            get
            {
                _getDependenciesExecutable ??= FindExecutable("get_dependencies");
                return _getDependenciesExecutable;
            }
        }

        public static string UpdateDependenciesExecutable
        {
            get
            {
                _updateDependenciesExecutable ??= FindExecutable("update_dependencies");
                return _updateDependenciesExecutable;
            }
        }

        public static bool IsGetDependenciesAvailable
        {
            get => !string.IsNullOrEmpty(GetDependenciesExecutable);
        }

        public static bool IsUpdateDependenciesAvailable
        {
            get => !string.IsNullOrEmpty(UpdateDependenciesExecutable);
        }

        /// <summary>
        ///     Runs `get_dependencies -f {repo}`.
        /// </summary>
        /// <returns>An error message on failure, or an empty string on success.</returns>
        public static Task<string> GetDependenciesAsync(string repo, ICommandLog log)
        {
            return RunAsync("get_dependencies", GetDependenciesExecutable, repo, log, "-f");
        }

        /// <summary>
        ///     Runs `update_dependencies {repo}`.
        /// </summary>
        /// <returns>An error message on failure, or an empty string on success.</returns>
        public static Task<string> UpdateDependenciesAsync(string repo, ICommandLog log)
        {
            return RunAsync("update_dependencies", UpdateDependenciesExecutable, repo, log);
        }

        private static async Task<string> RunAsync(string name, string exec, string repo, ICommandLog log, params string[] args)
        {
            if (string.IsNullOrEmpty(exec))
                return $"'{name}' was not found in `PATH`";

            var start = new ProcessStartInfo();
            start.FileName = exec;
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            start.ArgumentList.Add(repo);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            start.WorkingDirectory = repo;

            log?.AppendLine($"$ {name} {string.Join(' ', start.ArgumentList)}\n");

            using var proc = new Process();
            proc.StartInfo = start;

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    log?.AppendLine(e.Data);
            };

            var builder = new StringBuilder();
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    log?.AppendLine(e.Data);
                    builder.AppendLine(e.Data);
                }
            };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync().ConfigureAwait(false);

                log?.AppendLine(string.Empty);

                if (proc.ExitCode != 0)
                {
                    var errMsg = builder.ToString().Trim();
                    return string.IsNullOrEmpty(errMsg) ? $"{name} exited with code {proc.ExitCode}" : errMsg;
                }
            }
            catch (Exception e)
            {
                log?.AppendLine(e.Message);
                return e.Message;
            }

            return string.Empty;
        }

        private static string FindExecutable(string filename)
        {
            var candidates = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
                foreach (var ext in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    candidates.Add(filename + ext);
            }
            else
            {
                candidates.Add(filename);
            }

            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var paths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var path in paths)
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var test = Path.Combine(path.Trim('"'), candidate);
                        if (File.Exists(test))
                            return test;
                    }
                    catch
                    {
                        // Ignore malformed `PATH` entries.
                    }
                }
            }

            return string.Empty;
        }

        private static string _getDependenciesExecutable = null;
        private static string _updateDependenciesExecutable = null;
    }
}
