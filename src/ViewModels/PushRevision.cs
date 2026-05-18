using System;
using System.Linq;
using System.Threading.Tasks;
using SourceGit.Models;
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

        public bool CICancelLast
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
            if (CICancelLast)
            {
                ProgressDescription = $"Cancelling last CI pipelines for branch {RemoteBranch.FriendlyName} ...";
                await CIManager.CancelPipelinesForBranch(CI.GetGitlabRoute(_repo.Remotes), RemoteBranch.Name);
            }

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
                string remoteMessage = string.Join("\n    ", log.Content.Split('\n')
                    .Where(line => line.StartsWith("remote: "))
                    .Select(line => line.Substring(8).Trim())
                    .Where(line => !string.IsNullOrEmpty(line)));
                if (!string.IsNullOrEmpty(remoteMessage))
                {
                    Models.Notification.Send(_repo.FullPath, "Message from remote:\n\n    " + remoteMessage + "\n");
                }
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
