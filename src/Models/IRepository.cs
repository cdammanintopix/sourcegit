namespace SourceGit.Models
{
    public interface IRepository
    {
        bool MayHaveSubmodules();

        void RefreshBranches();
        void RefreshWorktrees();
        void RefreshTags();
        void RefreshCommits();
        void RefreshDependencies();
        void RefreshSubmodules();
        void RefreshWorkingCopyChanges();
        void RefreshStashes();
    }
}
