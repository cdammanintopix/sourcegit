using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
    public class Dependency
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
        ///     Branch currently checked out, as read from the working copy.
        /// </summary>
        [JsonIgnore]
        public string HeadBranch { get; private set; } = string.Empty;

        [JsonIgnore]
        public DependencyStatus Status { get; private set; } = DependencyStatus.Normal;

        [JsonIgnore]
        public bool IsGit => RepoType.Equals("git", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        ///     Only `git` dependencies that are actually cloned can be opened in SourceGit.
        /// </summary>
        [JsonIgnore]
        public bool CanBeOpened => IsGit && Status != DependencyStatus.NotCloned;

        [JsonIgnore]
        public bool IsDirty => Status == DependencyStatus.RevisionChanged;

        /// <summary>
        ///     Locates the working copy under the given IPX root folder and compares the revision that is
        ///     checked out with the one recorded in `dependencies.json`.
        /// </summary>
        public async Task ResolveAsync(string ipxRoot)
        {
            if (!string.IsNullOrEmpty(ipxRoot))
                FullPath = Path.GetFullPath(Path.Combine(ipxRoot, Name)).Replace('\\', '/').TrimEnd('/');

            if (string.IsNullOrEmpty(FullPath) || !Directory.Exists(FullPath))
            {
                Status = DependencyStatus.NotCloned;
                return;
            }

            if (!IsGit)
            {
                // Mercurial dependencies can neither be inspected nor opened by SourceGit.
                Status = DependencyStatus.Unsupported;
                return;
            }

            HeadSHA = await new Commands.QueryRevisionByRefName(FullPath, "HEAD")
                .GetResultAsync()
                .ConfigureAwait(false) ?? string.Empty;

            if (string.IsNullOrEmpty(HeadSHA))
            {
                Status = DependencyStatus.NotCloned;
            }
            else if (!HeadSHA.Equals(RevHash, StringComparison.OrdinalIgnoreCase))
            {
                Status = DependencyStatus.RevisionChanged;

                // Only shown by the tooltip on a mismatch, so it is not worth querying otherwise.
                HeadBranch = await new Commands.QueryCurrentBranch(FullPath)
                    .GetResultAsync()
                    .ConfigureAwait(false);
            }
            else
            {
                Status = DependencyStatus.Normal;
            }
        }
    }
}
