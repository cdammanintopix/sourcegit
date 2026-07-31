using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class UpdateDependencies : Popup
    {
        public UpdateDependencies(Repository repo)
        {
            _repo = repo;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Update dependencies ...";

            var log = _repo.CreateLog("Update Dependencies");
            Use(log);

            var err = await Models.IpxRevCtrl.UpdateDependenciesAsync(_repo.FullPath, log);
            log.Complete();

            if (!string.IsNullOrEmpty(err))
                _repo.SendNotification(err, true);

            _repo.MarkWorkingCopyDirtyManually();

            // Always close the popup. Failures are reported by the notification above and the log.
            return true;
        }

        private readonly Repository _repo = null;
    }
}
