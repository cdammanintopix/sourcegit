using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public enum DependencyStatus
    {
        Normal = 0,
        NotCloned,
        RevisionChanged,
        Unsupported,
    }

    /// <summary>
    ///     One entry of the `build/dependencies/dependencies.json` file managed by the `ipx-rev-ctrl`
    ///     package. See <see cref="IpxRevCtrl.LoadDependenciesAsync"/>.
    /// </summary>
    public class Dependency : ObservableObject
    {
        /// <summary>
        ///     Path of the dependency relative to the IPX root folder. e.g. `libs/axi4`.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("rev_hash")]
        public string RevHash { get; set; } = string.Empty;

        [JsonPropertyName("rev_num")]
        public string RevNum { get; set; } = string.Empty;

        [JsonPropertyName("rev_branch")]
        public string RevBranch { get; set; } = string.Empty;

        [JsonPropertyName("rev_date")]
        public string RevDate { get; set; } = string.Empty;

        [JsonPropertyName("repo_type")]
        public string RepoType { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string URL { get; set; } = string.Empty;

        /// <summary>
        ///     Absolute path of the dependency working copy. Empty when the IPX root folder was not found.
        /// </summary>
        [JsonIgnore]
        public string FullPath { get; private set; } = string.Empty;

        /// <summary>
        ///     Revision currently checked out, as read from the working copy.
        /// </summary>
        [JsonIgnore]
        public string HeadSHA { get; private set; } = string.Empty;

        /// <summary>
        ///     Branch currently checked out, as read from the working copy. Empty on a detached HEAD.
        /// </summary>
        [JsonIgnore]
        public string HeadBranch { get; private set; } = string.Empty;

        [JsonIgnore]
        public DependencyStatus Status { get; private set; } = DependencyStatus.Normal;

        /// <summary>
        ///     Number of tracked files modified in the working copy, compared to the revision it has
        ///     checked out. Untracked files are not counted, they are not a modification of a revision.
        /// </summary>
        [JsonIgnore]
        public int LocalChangesCount { get; private set; } = 0;

        [JsonIgnore]
        public bool HasLocalChanges => LocalChangesCount > 0;

        [JsonIgnore]
        public bool IsGit => RepoType.Equals("git", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        ///     Only `git` dependencies that are actually cloned can be opened in SourceGit.
        /// </summary>
        [JsonIgnore]
        public bool CanBeOpened => IsGit && Status != DependencyStatus.NotCloned;

        /// <summary>
        ///     Either the wrong revision is checked out, or the working copy has been modified.
        /// </summary>
        [JsonIgnore]
        public bool IsDirty => Status == DependencyStatus.RevisionChanged || HasLocalChanges;

        /// <summary>
        ///     False until the working copy has been read. Entries are listed as soon as
        ///     `dependencies.json` is parsed, so they are shown before their state is known.
        /// </summary>
        [JsonIgnore]
        public bool IsResolved { get; private set; } = false;

        /// <summary>
        ///     The expected revision is checked out and nothing was modified since.
        /// </summary>
        [JsonIgnore]
        public bool IsClean => IsResolved && Status == DependencyStatus.Normal && !HasLocalChanges;

        /// <summary>
        ///     Neither the green check nor the status badge is shown while the state is still unknown.
        /// </summary>
        [JsonIgnore]
        public bool HasStatusBadge => IsResolved && Status != DependencyStatus.Normal;

        /// <summary>
        ///     Raises the change notifications for everything <see cref="RefreshAsync"/> may have
        ///     updated. Must be called on the UI thread.
        /// </summary>
        public void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(HeadSHA));
            OnPropertyChanged(nameof(HeadBranch));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(LocalChangesCount));
            OnPropertyChanged(nameof(HasLocalChanges));
            OnPropertyChanged(nameof(IsResolved));
            OnPropertyChanged(nameof(CanBeOpened));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(IsClean));
            OnPropertyChanged(nameof(HasStatusBadge));
        }

        /// <summary>
        ///     Locates the working copy under the given IPX root folder, then reads its state.
        /// </summary>
        public async Task ResolveAsync(string ipxRoot)
        {
            if (!string.IsNullOrEmpty(ipxRoot))
                FullPath = Path.GetFullPath(Path.Combine(ipxRoot, Name)).Replace('\\', '/').TrimEnd('/');

            await RefreshAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///     Compares the revision checked out in <see cref="FullPath"/> with the one recorded in
        ///     `dependencies.json`, and counts the files modified since. Does not raise any change
        ///     notification, see <see cref="NotifyStateChanged"/>.
        /// </summary>
        public async Task RefreshAsync()
        {
            // `RefreshAsync` is also called to re-read an already resolved dependency, so nothing may
            // be carried over from the previous run.
            HeadSHA = string.Empty;
            HeadBranch = string.Empty;
            LocalChangesCount = 0;

            Status = await ReadStatusAsync().ConfigureAwait(false);
            IsResolved = true;
        }

        private async Task<DependencyStatus> ReadStatusAsync()
        {
            if (string.IsNullOrEmpty(FullPath) || !Directory.Exists(FullPath))
                return DependencyStatus.NotCloned;

            // Mercurial dependencies can neither be inspected nor opened by SourceGit.
            if (!IsGit)
                return DependencyStatus.Unsupported;

            var state = await new Commands.QueryHeadAndLocalChanges(FullPath)
                .GetResultAsync()
                .ConfigureAwait(false);

            if (state == null || string.IsNullOrEmpty(state.SHA))
                return DependencyStatus.NotCloned;

            HeadSHA = state.SHA;
            HeadBranch = state.Branch;
            LocalChangesCount = state.LocalChanges;

            return HeadSHA.Equals(RevHash, StringComparison.OrdinalIgnoreCase)
                ? DependencyStatus.Normal
                : DependencyStatus.RevisionChanged;
        }
    }
}
