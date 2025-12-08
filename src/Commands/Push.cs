using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Push : Command
    {
        public Push(string repo, string local, string remote, string remoteBranch, bool withTags, bool checkSubmodules, bool track, bool force, bool createMR = false, bool draftMR = false, bool ciSkip = false, string ciArgs ="")
        {
            _remote = remote;

            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(1024);
            builder.Append("push --progress --verbose ");
            if (withTags)
                builder.Append("--tags ");
            if (checkSubmodules)
                builder.Append("--recurse-submodules=check ");
            if (track)
                builder.Append("-u ");
            if (force)
                builder.Append("--force-with-lease ");
            if (createMR) {
                builder.Append("-o merge_request.create -o merge_request.assign=me ");
                if (draftMR)
                    builder.Append("-o merge_request.draft ");
            }
            if (ciSkip)
                builder.Append("-o ci.skip ");
            else if (ciArgs.Length > 0)
                builder.Append("-o ci.input=\"ci-args=" + ciArgs.Replace("\"", "\\\"") + "\" ");

            builder.Append(remote).Append(' ').Append(local).Append(':').Append(remoteBranch);
            Args = builder.ToString();
        }

        public Push(string repo, string remote, string refname, bool isDelete, string ciArgs="")
        {
            _remote = remote;

            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder(512);
            builder.Append("push ");
            if (isDelete)
                builder.Append("--delete ");
            if (ciArgs.Length > 0)
                builder.Append("-o ci.input=\"ci-args=" + ciArgs.Replace("\"", "\\\"") + "\" ");
            builder.Append(remote).Append(' ').Append(refname);

            Args = builder.ToString();
        }

        public async Task<bool> RunAsync()
        {
            SSHKey = await new Config(WorkingDirectory).GetAsync($"remote.{_remote}.sshkey").ConfigureAwait(false);
            return await ExecAsync().ConfigureAwait(false);
        }

        private readonly string _remote;
    }
}
