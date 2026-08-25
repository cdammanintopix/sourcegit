using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    public class Watcher : IDisposable
    {
        public class LockContext : IDisposable
        {
            public LockContext(Watcher target)
            {
                _target = target;
                Interlocked.Increment(ref _target._lockCount);
            }

            public void Dispose()
            {
                Interlocked.Decrement(ref _target._lockCount);
            }

            private Watcher _target;
        }

        /// <summary>
        ///     Keeps one `FileSystemWatcher` per dependency `.git` folder, shared by every open
        ///     repository: several of them usually depend on the same libraries. Each subscriber still
        ///     gets its own notification, since the revision they expect differs.
        /// </summary>
        private static class DependencyGitDirPool
        {
            private class Entry
            {
                public FileSystemWatcher Watcher;
                public readonly List<Watcher> Subscribers = [];
            }

            public static void Subscribe(string gitDir, Watcher subscriber)
            {
                FileSystemWatcher started = null;

                lock (_lock)
                {
                    if (!_entries.TryGetValue(gitDir, out var entry))
                    {
                        entry = new Entry();

                        var watcher = new FileSystemWatcher();
                        watcher.Path = gitDir;
                        watcher.Filter = "*";
                        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
                        watcher.IncludeSubdirectories = true;
                        watcher.Created += (_, e) => Dispatch(entry, e);
                        watcher.Renamed += (_, e) => Dispatch(entry, e);
                        watcher.Changed += (_, e) => Dispatch(entry, e);
                        watcher.Deleted += (_, e) => Dispatch(entry, e);
                        watcher.EnableRaisingEvents = false;

                        entry.Watcher = watcher;
                        _entries.Add(gitDir, entry);
                        started = watcher;
                    }

                    entry.Subscribers.Add(subscriber);
                }

                if (started == null)
                    return;

                // Starts the filesystem watcher in another thread to avoid UI blocking
                Task.Run(() =>
                {
                    try
                    {
                        started.EnableRaisingEvents = true;
                    }
                    catch
                    {
                        // Ignore exceptions. This may occur while `Unsubscribe` disposes it.
                    }
                });
            }

            public static void Unsubscribe(string gitDir, Watcher subscriber)
            {
                FileSystemWatcher disposing;

                lock (_lock)
                {
                    if (!_entries.TryGetValue(gitDir, out var entry))
                        return;

                    entry.Subscribers.Remove(subscriber);
                    if (entry.Subscribers.Count > 0)
                        return;

                    _entries.Remove(gitDir);
                    disposing = entry.Watcher;
                }

                disposing.EnableRaisingEvents = false;
                disposing.Dispose();
            }

            private static void Dispatch(Entry entry, FileSystemEventArgs e)
            {
                Watcher[] subscribers;
                lock (_lock)
                    subscribers = entry.Subscribers.ToArray();

                foreach (var subscriber in subscribers)
                    subscriber.OnDependencyGitDirChanged(e);
            }

            private static readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
            private static readonly object _lock = new();
        }

        public Watcher(IRepository repo, string fullpath, string gitDir)
        {
            _repo = repo;
            _root = new DirectoryInfo(fullpath).FullName;
            _watchers = new List<FileSystemWatcher>();

            var testGitDir = new DirectoryInfo(Path.Combine(fullpath, ".git")).FullName;
            var desiredDir = new DirectoryInfo(gitDir).FullName;
            if (testGitDir.Equals(desiredDir, StringComparison.Ordinal))
            {
                var combined = new FileSystemWatcher();
                combined.Path = fullpath;
                combined.Filter = "*";
                combined.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
                combined.IncludeSubdirectories = true;
                combined.Created += OnRepositoryChanged;
                combined.Renamed += OnRepositoryChanged;
                combined.Changed += OnRepositoryChanged;
                combined.Deleted += OnRepositoryChanged;
                combined.EnableRaisingEvents = false;

                _watchers.Add(combined);
            }
            else
            {
                var wc = new FileSystemWatcher();
                wc.Path = fullpath;
                wc.Filter = "*";
                wc.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
                wc.IncludeSubdirectories = true;
                wc.Created += OnWorkingCopyChanged;
                wc.Renamed += OnWorkingCopyChanged;
                wc.Changed += OnWorkingCopyChanged;
                wc.Deleted += OnWorkingCopyChanged;
                wc.EnableRaisingEvents = false;

                var git = new FileSystemWatcher();
                git.Path = gitDir;
                git.Filter = "*";
                git.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
                git.IncludeSubdirectories = true;
                git.Created += OnGitDirChanged;
                git.Renamed += OnGitDirChanged;
                git.Changed += OnGitDirChanged;
                git.Deleted += OnGitDirChanged;
                git.EnableRaisingEvents = false;

                _watchers.Add(wc);
                _watchers.Add(git);
            }

            _timer = new Timer(Tick, null, 100, 100);

            // Starts filesystem watchers in another thread to avoid UI blocking
            Task.Run(() =>
            {
                try
                {
                    foreach (var watcher in _watchers)
                        watcher.EnableRaisingEvents = true;
                }
                catch
                {
                    // Ignore exceptions. This may occur while `Dispose` is called.
                }
            });
        }

        public IDisposable Lock()
        {
            return new LockContext(this);
        }

        public void MarkBranchUpdated()
        {
            Interlocked.Exchange(ref _updateBranch, 0);
            Interlocked.Exchange(ref _updateWC, 0);
        }

        public void MarkTagUpdated()
        {
            Interlocked.Exchange(ref _updateTags, 0);
        }

        public void MarkWorkingCopyUpdated()
        {
            Interlocked.Exchange(ref _updateWC, 0);
        }

        public void MarkStashUpdated()
        {
            Interlocked.Exchange(ref _updateStashes, 0);
        }

        public void MarkSubmodulesUpdated()
        {
            Interlocked.Exchange(ref _updateSubmodules, 0);
        }

        public void MarkDependenciesUpdated()
        {
            Interlocked.Exchange(ref _updateDependencies, 0);
        }

        /// <summary>
        ///     Watches the `.git` folder of each dependency, so that checking out another revision in one
        ///     of them - from another tab, another tool or a terminal - updates their state right away.
        ///     Only the `.git` folder is watched: local edits in a dependency working copy do not change
        ///     the revision it has checked out.
        /// </summary>
        public void SetDependencies(List<Dependency> dependencies)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dep in dependencies)
            {
                if (!dep.IsGit || string.IsNullOrEmpty(dep.FullPath))
                    continue;

                // A plain clone has a `.git` folder. Anything else (a worktree or a submodule, which
                // store a `gitdir:` pointer file instead) is left out.
                var gitDir = Path.Combine(dep.FullPath, ".git");
                if (Directory.Exists(gitDir))
                    wanted.Add(new DirectoryInfo(gitDir).FullName);
            }

            if (wanted.SetEquals(_dependencyGitDirs))
                return;

            foreach (var gitDir in _dependencyGitDirs)
            {
                if (!wanted.Contains(gitDir))
                    DependencyGitDirPool.Unsubscribe(gitDir, this);
            }

            foreach (var gitDir in wanted)
            {
                if (!_dependencyGitDirs.Contains(gitDir))
                    DependencyGitDirPool.Subscribe(gitDir, this);
            }

            _dependencyGitDirs = wanted;
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();

            foreach (var gitDir in _dependencyGitDirs)
                DependencyGitDirPool.Unsubscribe(gitDir, this);
            _dependencyGitDirs = [];

            _timer.Dispose();
            _timer = null;
        }

        private void Tick(object sender)
        {
            if (Interlocked.Read(ref _lockCount) > 0)
                return;

            var now = DateTime.Now.ToFileTime();
            var refreshCommits = false;
            var refreshSubmodules = false;
            var refreshDependencies = false;
            var refreshWC = false;

            var oldUpdateBranch = Interlocked.Exchange(ref _updateBranch, -1);
            if (oldUpdateBranch > 0)
            {
                if (now > oldUpdateBranch)
                {
                    refreshCommits = true;
                    refreshSubmodules = _repo.MayHaveSubmodules();
                    refreshDependencies = true; // `dependencies.json` is a tracked file, so its content follows `HEAD`.
                    refreshWC = true;

                    _repo.RefreshBranches();
                    _repo.RefreshWorktrees();
                }
                else
                {
                    Interlocked.CompareExchange(ref _updateBranch, oldUpdateBranch, -1);
                }
            }

            if (refreshWC)
            {
                Interlocked.Exchange(ref _updateWC, -1);
                _repo.RefreshWorkingCopyChanges();
            }
            else
            {
                var oldUpdateWC = Interlocked.Exchange(ref _updateWC, -1);
                if (oldUpdateWC > 0)
                {
                    if (now > oldUpdateWC)
                        _repo.RefreshWorkingCopyChanges();
                    else
                        Interlocked.CompareExchange(ref _updateWC, oldUpdateWC, -1);
                }
            }

            if (refreshSubmodules)
            {
                Interlocked.Exchange(ref _updateSubmodules, -1);
                _repo.RefreshSubmodules();
            }
            else
            {
                var oldUpdateSubmodule = Interlocked.Exchange(ref _updateSubmodules, -1);
                if (oldUpdateSubmodule > 0)
                {
                    if (now > oldUpdateSubmodule)
                        _repo.RefreshSubmodules();
                    else
                        Interlocked.CompareExchange(ref _updateSubmodules, oldUpdateSubmodule, -1);
                }
            }

            if (refreshDependencies)
            {
                Interlocked.Exchange(ref _updateDependencies, -1);
                _repo.RefreshDependencies();
            }
            else
            {
                var oldUpdateDependencies = Interlocked.Exchange(ref _updateDependencies, -1);
                if (oldUpdateDependencies > 0)
                {
                    if (now > oldUpdateDependencies)
                        _repo.RefreshDependencies();
                    else
                        Interlocked.CompareExchange(ref _updateDependencies, oldUpdateDependencies, -1);
                }
            }

            var oldUpdateStashes = Interlocked.Exchange(ref _updateStashes, -1);
            if (oldUpdateStashes > 0)
            {
                if (now > oldUpdateStashes)
                    _repo.RefreshStashes();
                else
                    Interlocked.CompareExchange(ref _updateStashes, oldUpdateStashes, -1);
            }

            var oldUpdateTags = Interlocked.Exchange(ref _updateTags, -1);
            if (oldUpdateTags > 0)
            {
                if (now > oldUpdateTags)
                {
                    refreshCommits = true;
                    _repo.RefreshTags();
                }
                else
                {
                    Interlocked.CompareExchange(ref _updateTags, oldUpdateTags, -1);
                }
            }

            if (refreshCommits)
                _repo.RefreshCommits();
        }

        private void OnRepositoryChanged(object o, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name) || e.Name.Equals(".git", StringComparison.Ordinal))
                return;

            var name = e.Name.Replace('\\', '/').TrimEnd('/');
            if (name.EndsWith("/.git", StringComparison.Ordinal))
                return;

            if (name.StartsWith(".git/", StringComparison.Ordinal))
                HandleGitDirFileChanged(name.Substring(5));
            else
                HandleWorkingCopyFileChanged(name, e.FullPath);
        }

        private void OnGitDirChanged(object o, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name))
                return;

            var name = e.Name.Replace('\\', '/').TrimEnd('/');
            HandleGitDirFileChanged(name);
        }

        private void OnDependencyGitDirChanged(FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name))
                return;

            var name = e.Name.Replace('\\', '/').TrimEnd('/');
            if (name.EndsWith(".lock", StringComparison.Ordinal) ||
                name.Contains("fsmonitor--daemon/", StringComparison.Ordinal) ||
                name.StartsWith("lfs/", StringComparison.Ordinal) ||
                name.StartsWith("objects/", StringComparison.Ordinal))
                return;

            // `HEAD` covers a checkout, the refs cover the branch it points at moving (a pull for example).
            if (name.Equals("HEAD", StringComparison.Ordinal) ||
                name.Equals("packed-refs", StringComparison.Ordinal) ||
                name.StartsWith("refs/", StringComparison.Ordinal) ||
                name.StartsWith("reftable/", StringComparison.Ordinal))
                Interlocked.Exchange(ref _updateDependencies, DateTime.Now.AddSeconds(1).ToFileTime());
        }

        private void OnWorkingCopyChanged(object o, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name))
                return;

            var name = e.Name.Replace('\\', '/').TrimEnd('/');
            if (name.Equals(".git", StringComparison.Ordinal) ||
                name.StartsWith(".git/", StringComparison.Ordinal) ||
                name.EndsWith("/.git", StringComparison.Ordinal))
                return;

            HandleWorkingCopyFileChanged(name, e.FullPath);
        }

        private void HandleGitDirFileChanged(string name)
        {
            if (name.Contains("fsmonitor--daemon/", StringComparison.Ordinal) ||
                name.EndsWith(".lock", StringComparison.Ordinal) ||
                name.StartsWith("lfs/", StringComparison.Ordinal))
                return;

            if (name.StartsWith("modules", StringComparison.Ordinal))
            {
                if (name.EndsWith("/HEAD", StringComparison.Ordinal) ||
                    name.EndsWith("/ORIG_HEAD", StringComparison.Ordinal))
                {
                    var desired = DateTime.Now.AddSeconds(1).ToFileTime();
                    Interlocked.Exchange(ref _updateSubmodules, desired);
                    Interlocked.Exchange(ref _updateWC, desired);
                }
            }
            else if (name.Equals("MERGE_HEAD", StringComparison.Ordinal) ||
                name.Equals("AUTO_MERGE", StringComparison.Ordinal))
            {
                if (_repo.MayHaveSubmodules())
                    Interlocked.Exchange(ref _updateSubmodules, DateTime.Now.AddSeconds(1).ToFileTime());
            }
            else if (name.StartsWith("refs/tags", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref _updateTags, DateTime.Now.AddSeconds(.5).ToFileTime());
            }
            else if (name.StartsWith("refs/stash", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref _updateStashes, DateTime.Now.AddSeconds(.5).ToFileTime());
            }
            else if (name.Equals("HEAD", StringComparison.Ordinal) ||
                name.Equals("BISECT_START", StringComparison.Ordinal) ||
                name.StartsWith("refs/heads/", StringComparison.Ordinal) ||
                name.StartsWith("refs/remotes/", StringComparison.Ordinal) ||
                (name.StartsWith("worktrees/", StringComparison.Ordinal) && name.EndsWith("/HEAD", StringComparison.Ordinal)))
            {
                Interlocked.Exchange(ref _updateBranch, DateTime.Now.AddSeconds(.5).ToFileTime());
            }
            else if (name.StartsWith("reftable/", StringComparison.Ordinal))
            {
                var desired = DateTime.Now.AddSeconds(.5).ToFileTime();
                Interlocked.Exchange(ref _updateBranch, desired);
                Interlocked.Exchange(ref _updateTags, desired);
                Interlocked.Exchange(ref _updateStashes, desired);
            }
            else if (name.StartsWith("objects/", StringComparison.Ordinal) || name.Equals("index", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref _updateWC, DateTime.Now.AddSeconds(1).ToFileTime());
            }
        }

        private void HandleWorkingCopyFileChanged(string name, string fullpath)
        {
            if (name.StartsWith(".vs/", StringComparison.Ordinal))
                return;

            if (name.Equals("build/dependencies/dependencies.json", StringComparison.Ordinal))
            {
                var desired = DateTime.Now.AddSeconds(1).ToFileTime();
                Interlocked.Exchange(ref _updateDependencies, desired);
                Interlocked.Exchange(ref _updateWC, desired);
                return;
            }

            if (name.Equals(".gitmodules", StringComparison.Ordinal))
            {
                var desired = DateTime.Now.AddSeconds(1).ToFileTime();
                Interlocked.Exchange(ref _updateSubmodules, desired);
                Interlocked.Exchange(ref _updateWC, desired);
                return;
            }

            var dir = Directory.Exists(fullpath) ? fullpath : Path.GetDirectoryName(fullpath);
            if (IsInSubmodule(dir))
            {
                Interlocked.Exchange(ref _updateSubmodules, DateTime.Now.AddSeconds(1).ToFileTime());
                return;
            }

            Interlocked.Exchange(ref _updateWC, DateTime.Now.AddSeconds(1).ToFileTime());
        }

        private bool IsInSubmodule(string folder)
        {
            if (string.IsNullOrEmpty(folder) || folder.Equals(_root, StringComparison.Ordinal))
                return false;

            if (File.Exists($"{folder}/.git"))
                return true;

            return IsInSubmodule(Path.GetDirectoryName(folder));
        }

        private readonly IRepository _repo;
        private readonly string _root;
        private List<FileSystemWatcher> _watchers;
        private HashSet<string> _dependencyGitDirs = new(StringComparer.Ordinal);
        private Timer _timer;

        private long _lockCount;
        private long _updateWC;
        private long _updateBranch;
        private long _updateSubmodules;
        private long _updateDependencies;
        private long _updateStashes;
        private long _updateTags;
    }
}
