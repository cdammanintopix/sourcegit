using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class ResetDependency : Popup
    {
        public Models.Dependency Dependency
        {
            get;
        }

        public ResetDependency(Repository repo, Models.Dependency dep)
        {
            _repo = repo;
            Dependency = dep;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = $"Reset dependency '{Dependency.Name}' ...";

            var log = _repo.CreateLog($"Reset Dependency '{Dependency.Name}'");
            Use(log);

            var err = await Models.IpxRevCtrl.GetDependenciesAsync(_repo.FullPath, log, Dependency.Name);
            log.Complete();

            if (!string.IsNullOrEmpty(err))
                _repo.SendNotification(err, true);

            _repo.MarkWorkingCopyDirtyManually();
            _repo.MarkDependenciesDirtyManually();

            // Always close the popup. Failures are reported by the notification above and the log.
            return true;
        }

        private readonly Repository _repo = null;
    }
}
