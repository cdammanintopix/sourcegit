using System;
using System.Threading.Tasks;
using SourceGit.Views;

namespace SourceGit.ViewModels
{
    public class PushRevision : Popup
    {
        public Models.Commit Revision
        {
            get;
        }

        public Models.Branch RemoteBranch
        {
            get;
        }

        public bool Force
        {
            get;
            set;
        }

        public bool CISkip
        {
            get => _ciSkip;
            set
            {
                if (SetProperty(ref _ciSkip, value))
                    OnPropertyChanged(nameof(IsCIArgsVisible));
            }
        }

        public bool IsCIArgsVisible
        {
            get => !_ciSkip;
        }

        public string CIArgs { get; set; } = "";

        public PushRevision(Repository repo, Models.Commit revision, Models.Branch remoteBranch)
        {
            _repo = repo;
            Revision = revision;
            RemoteBranch = remoteBranch;
            Force = false;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = $"Push {Revision.SHA.AsSpan(0, 10)} -> {RemoteBranch.FriendlyName} ...";

            var log = _repo.CreateLog("Push Revision");
            Use(log);

            var succ = await new Commands.Push(
                _repo.FullPath,
                Revision.SHA,
                RemoteBranch.Remote,
                RemoteBranch.Name,
                false,
                false,
                false,
                Force, _ciSkip, CIArgs).Use(log).RunAsync();

            log.Complete();

            if (succ)
            {
                // Trigger CI status refresh
                for (int i = 5; i > 0; i--)
                {
                    ProgressDescription = "Refresh CI status... (" + i + ")";
                    await Task.Delay(1000);
                }
                CI.Refresh(_repo.Remotes, Revision.SHA);
            }

            return succ;
        }

        private readonly Repository _repo;
        private bool _ciSkip = false;
    }
}
