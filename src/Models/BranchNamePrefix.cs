using System;

namespace SourceGit.Models
{
    /// <summary>
    ///     Helpers to add/remove the `[branch name]` prefix used in commit messages.
    /// </summary>
    public static class BranchNamePrefix
    {
        public static bool IsSupported(Branch branch)
        {
            return branch is { IsDetachedHead: false } && !string.IsNullOrEmpty(branch.Name);
        }

        public static string Make(Branch branch)
        {
            return $"[{branch.Name}]";
        }

        public static bool IsPrepended(string message, Branch branch)
        {
            return IsSupported(branch) &&
                !string.IsNullOrEmpty(message) &&
                message.StartsWith(Make(branch), StringComparison.Ordinal);
        }

        public static string Prepend(string message, Branch branch)
        {
            if (!IsSupported(branch))
                return message ?? string.Empty;

            message ??= string.Empty;

            var prefix = Make(branch);
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return message;

            // Do not add a separator when the message already starts with another tag.
            return message.StartsWith('[') ? prefix + message : prefix + " " + message;
        }

        public static string Remove(string message, Branch branch)
        {
            if (!IsPrepended(message, branch))
                return message ?? string.Empty;

            var rest = message.Substring(Make(branch).Length);
            return rest.StartsWith(' ') ? rest.Substring(1) : rest;
        }
    }
}
