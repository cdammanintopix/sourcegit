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

            var tagName = "run_" + Commit.SHA.Substring(0, 8);
            var cmd = new Commands.Tag(_repo.FullPath, tagName).Use(log);
            var succ = false;
            succ = await cmd.AddAsync(Commit.SHA);

            if (succ && remotes != null)
            {
                foreach (var remote in remotes)
                {
                    if (remote.URL.Contains("gitlab.intopix.com"))
                    {
                        if (await new Commands.Push(_repo.FullPath, remote.Name, $"refs/tags/{tagName}", false, CIArgs)
                            .Use(log)
                            .RunAsync())
                            await new Commands.Push(_repo.FullPath, remote.Name, $"refs/tags/{tagName}", true)
                            .Use(log)
                            .RunAsync();
                    }
                }
            }

            if (succ)
            {
                succ = await new Commands.Tag(_repo.FullPath, tagName)
                    .Use(log)
                    .DeleteAsync();
            }

            log.Complete();

            // Trigger refresh
            ProgressDescription = "Refresh CI status...";
            await Task.Delay(3000);
            var req = CI.GetReq(remotes, Commit.SHA);
            if (!string.IsNullOrEmpty(req))
                Models.CIManager.Instance.Request(req, true);

            return succ;
        }

        private readonly Repository _repo = null;
    }
}
