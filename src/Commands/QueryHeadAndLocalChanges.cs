using System;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Reads the revision a working copy has checked out, the branch it is on and how many tracked
    ///     files were modified since - all from a single `git status`.
    /// </summary>
    public class QueryHeadAndLocalChanges : Command
    {
        public class HeadState
        {
            public string SHA { get; set; } = string.Empty;

            /// <summary>
            ///     Empty on a detached HEAD, like `git branch --show-current`.
            /// </summary>
            public string Branch { get; set; } = string.Empty;

            public int LocalChanges { get; set; } = 0;
        }

        public QueryHeadAndLocalChanges(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;

            // `-uno` because an untracked file is not a modification of the checked out revision.
            Args = "--no-optional-locks status --porcelain=v2 --branch -uno --ignore-submodules=all";
        }

        /// <summary>
        ///     Returns null when the folder is not a git repository.
        /// </summary>
        public async Task<HeadState> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return null;

            var state = new HeadState();
            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
                {
                    var oid = line.Substring(13).Trim();
                    if (!oid.Equals("(initial)", StringComparison.Ordinal))
                        state.SHA = oid;
                }
                else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                {
                    var head = line.Substring(14).Trim();
                    if (!head.Equals("(detached)", StringComparison.Ordinal))
                        state.Branch = head;
                }
                else if (!line.StartsWith("# ", StringComparison.Ordinal))
                {
                    // Changed (`1`, `2`) and unmerged (`u`) entries. Untracked and ignored ones are
                    // filtered out by `-uno`.
                    state.LocalChanges++;
                }
            }

            return state;
        }
    }
}
