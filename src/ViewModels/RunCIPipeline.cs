using System.Threading.Tasks;
using SourceGit.Views;

namespace SourceGit.ViewModels
{
    public class RunCIPipeline : Popup
    {
        public Models.Commit Commit { get; }
        public string CIArgs { get; set; } = "";

        public RunCIPipeline(Repository repo, Models.Commit commit)
        {
            _repo = repo;
            Commit = commit;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Run pipeline...";

            var remotes = _repo.Remotes;
            var log = _repo.CreateLog("Run pipeline");
            Use(log);

            var branchName = "run_" + Commit.SHA.Substring(0, 8);
            var cmd = new Commands.Branch(_repo.FullPath, branchName).Use(log);
            var succ = false;
            succ = await cmd.CreateAsync(Commit.SHA, true);

            if (succ && remotes != null)
            {
                foreach (var remote in remotes)
                {
                    if (remote.URL.Contains("gitlab.intopix.com"))
                    {
                        if (await new Commands.Push(_repo.FullPath, remote.Name, $"refs/heads/{branchName}", false, CIArgs)
                            .Use(log)
                            .RunAsync())
                            await new Commands.Push(_repo.FullPath, remote.Name, $"refs/heads/{branchName}", true)
                            .Use(log)
                            .RunAsync();
                    }
                }
            }

            if (succ)
            {
                succ = await new Commands.Branch(_repo.FullPath, branchName)
                    .Use(log)
                    .DeleteLocalAsync();
            }

            log.Complete();

            if (succ)
            {
                // Trigger CI status refresh
                CI.QueueForNextRefresh(remotes, Commit.SHA);
            }

            return succ;
        }

        private readonly Repository _repo = null;
    }
}
