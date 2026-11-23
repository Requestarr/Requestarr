using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Requestarr.Core.Test.Framework
{
    // Files in the repository checkout, for tests that read the docs, the workflows or recorded data.
    public static class RepoFiles
    {
        public static string Path(params string[] parts)
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            // A checkout's .git is a folder, or a file in a worktree.
            while (dir != null && !System.IO.Path.Exists(System.IO.Path.Combine(dir.FullName, ".git")))
            {
                dir = dir.Parent;
            }

            return System.IO.Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        }
    }
}
