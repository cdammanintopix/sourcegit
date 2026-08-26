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
        void RefreshDependency(string fullPath);
        void RefreshSubmodules();
        void RefreshWorkingCopyChanges();
        void RefreshStashes();
    }
}
