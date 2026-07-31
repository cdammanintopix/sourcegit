using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
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

        public static string SearchIpxRootExecutable
        {
            get
            {
                _searchIpxRootExecutable ??= FindExecutable("search_ipxroot");
                return _searchIpxRootExecutable;
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

        public static bool IsSearchIpxRootAvailable
        {
            get => !string.IsNullOrEmpty(SearchIpxRootExecutable);
        }

        /// <summary>
        ///     True when the whole `ipx-rev-ctrl` toolset is installed.
        /// </summary>
        public static bool IsAvailable
        {
            get => IsGetDependenciesAvailable && IsUpdateDependenciesAvailable && IsSearchIpxRootAvailable;
        }

        public static string GetDependenciesFile(string repo)
        {
            return Path.Combine(repo, "build", "dependencies", "dependencies.json");
        }

        /// <summary>
        ///     Tests if the `DEPENDENCIES` section makes sense for the given repository, that is if the
        ///     `ipx-rev-ctrl` toolset is installed and if the repository declares dependencies.
        /// </summary>
        public static bool SupportsDependencies(string repo)
        {
            if (!IsAvailable)
                return false;

            var info = new FileInfo(GetDependenciesFile(repo));
            return info.Exists && info.Length > 2;
        }

        /// <summary>
        ///     Runs `get_dependencies -f {repo}`, restricted to a single dependency when
        ///     <paramref name="only"/> is given.
        /// </summary>
        /// <returns>An error message on failure, or an empty string on success.</returns>
        public static Task<string> GetDependenciesAsync(string repo, ICommandLog log, string only = null)
        {
            var args = string.IsNullOrEmpty(only) ? new[] { "-f" } : ["-f", "--only", only];
            return RunAsync("get_dependencies", GetDependenciesExecutable, repo, log, args);
        }

        /// <summary>
        ///     Runs `update_dependencies {repo}`.
        /// </summary>
        /// <returns>An error message on failure, or an empty string on success.</returns>
        public static Task<string> UpdateDependenciesAsync(string repo, ICommandLog log)
        {
            return RunAsync("update_dependencies", UpdateDependenciesExecutable, repo, log);
        }

        /// <summary>
        ///     Runs `search_ipxroot {repo}` to locate the IPX root folder. Dependency paths listed in
        ///     `dependencies.json` are relative to it.
        /// </summary>
        /// <returns>The IPX root folder, or an empty string when the repository is not inside one.</returns>
        public static async Task<string> SearchIpxRootAsync(string repo)
        {
            if (!IsSearchIpxRootAvailable)
                return string.Empty;

            var (_, stdout) = await ExecAsync(SearchIpxRootExecutable, repo, null, true).ConfigureAwait(false);
            return stdout.Trim();
        }

        /// <summary>
        ///     Reads `build/dependencies/dependencies.json` and resolves the state of each dependency.
        /// </summary>
        public static async Task<List<Dependency>> LoadDependenciesAsync(string repo)
        {
            if (!SupportsDependencies(repo))
                return [];

            List<Dependency> deps;
            try
            {
                using var stream = File.OpenRead(GetDependenciesFile(repo));
                deps = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.ListDependency) ?? [];
            }
            catch
            {
                return [];
            }

            var root = await SearchIpxRootAsync(repo).ConfigureAwait(false);
            var self = string.Empty;
            if (!string.IsNullOrEmpty(root))
            {
                try
                {
                    self = Path.GetRelativePath(root, repo).Replace('\\', '/');
                }
                catch
                {
                    // Keep `self` empty, no entry will be filtered out.
                }
            }

            var validated = new List<Dependency>();
            foreach (var dep in deps)
            {
                if (string.IsNullOrEmpty(dep.Name))
                    continue;

                // A repository never depends on itself.
                if (dep.Name.Equals(self, StringComparison.OrdinalIgnoreCase))
                    continue;

                validated.Add(dep);
            }

            // Resolving a dependency spawns a `git` process, and there are usually dozens of them living
            // on a network share. Run them concurrently, but keep the number of processes reasonable.
            using var limit = new SemaphoreSlim(Math.Min(8, Environment.ProcessorCount));
            await Task.WhenAll(validated.ConvertAll(async dep =>
            {
                await limit.WaitAsync().ConfigureAwait(false);
                try
                {
                    await dep.ResolveAsync(root).ConfigureAwait(false);
                }
                finally
                {
                    limit.Release();
                }
            })).ConfigureAwait(false);

            validated.Sort((l, r) => NumericSort.Compare(l.Name, r.Name));
            return validated;
        }

        private static async Task<string> RunAsync(string name, string exec, string repo, ICommandLog log, params string[] args)
        {
            if (string.IsNullOrEmpty(exec))
                return $"'{name}' was not found in `PATH`";

            log?.AppendLine($"$ {name} {repo} {string.Join(' ', args)}\n");

            string error;
            try
            {
                (error, _) = await ExecAsync(exec, repo, log, false, args).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                log?.AppendLine(e.Message);
                return e.Message;
            }

            log?.AppendLine(string.Empty);
            return error;
        }

        /// <summary>
        ///     Runs one of the `ipx-rev-ctrl` executables with `{repo}` as its last argument.
        /// </summary>
        /// <returns>
        ///     The error message (empty on success) and, when <paramref name="captureStdOut"/> is set,
        ///     the standard output.
        /// </returns>
        private static async Task<(string Error, string StdOut)> ExecAsync(string exec, string repo, ICommandLog log, bool captureStdOut, params string[] args)
        {
            var start = new ProcessStartInfo();
            start.FileName = exec;
            start.ArgumentList.Add(repo);
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            start.WorkingDirectory = repo;

            using var proc = new Process();
            proc.StartInfo = start;

            var stdout = new StringBuilder();
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                if (captureStdOut)
                    stdout.AppendLine(e.Data);
                else
                    log?.AppendLine(e.Data);
            };

            var stderr = new StringBuilder();
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    log?.AppendLine(e.Data);
                    stderr.AppendLine(e.Data);
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync().ConfigureAwait(false);

            var error = string.Empty;
            if (proc.ExitCode != 0)
            {
                var errMsg = stderr.ToString().Trim();
                error = string.IsNullOrEmpty(errMsg) ? $"{Path.GetFileNameWithoutExtension(exec)} exited with code {proc.ExitCode}" : errMsg;
            }

            return (error, stdout.ToString());
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
        private static string _searchIpxRootExecutable = null;
    }
}
