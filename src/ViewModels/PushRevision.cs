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
        public bool CreateMR
        {
            get => _createMR;
            set
            {
                if (SetProperty(ref _createMR, value))
                    OnPropertyChanged(nameof(IsDraftMRVisible));
            }
        }

        public bool IsDraftMRVisible
        {
            get => _createMR;
        }

        public bool DraftMR
        {
            get;
            set;
        } = true;

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
                Force, CreateMR, DraftMR, CISkip, CIArgs).Use(log).RunAsync();

            log.Complete();

            if (succ)
            {
                // Trigger CI status refresh
                CI.QueueForNextRefresh(_repo.Remotes, Revision.SHA);
            }

            return succ;
        }

        private readonly Repository _repo;
        private bool _createMR = false;
        private bool _ciSkip = false;
    }
}
