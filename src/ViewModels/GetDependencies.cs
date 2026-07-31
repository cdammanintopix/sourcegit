using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class GetDependencies : Popup
    {
        public GetDependencies(Repository repo)
        {
            _repo = repo;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Get dependencies ...";

            var log = _repo.CreateLog("Get Dependencies");
            Use(log);

            var err = await Models.IpxRevCtrl.GetDependenciesAsync(_repo.FullPath, log);
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
