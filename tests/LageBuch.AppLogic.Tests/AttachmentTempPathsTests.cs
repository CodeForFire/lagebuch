using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class AttachmentTempPathsTests
{
    [Fact]
    public void Root_is_a_lagebuch_folder_under_the_system_temp_directory()
    {
        Assert.Equal(Path.Join(Path.GetTempPath(), "lagebuch"), AttachmentTempPaths.Root);
    }

    [Fact]
    public void CreateOpenDirectory_creates_a_fresh_directory_under_the_root_every_time()
    {
        var first = AttachmentTempPaths.CreateOpenDirectory();
        var second = AttachmentTempPaths.CreateOpenDirectory();
        try
        {
            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));
            Assert.NotEqual(first, second);
            Assert.Equal(AttachmentTempPaths.Root, Path.GetDirectoryName(first));
            Assert.Equal(AttachmentTempPaths.Root, Path.GetDirectoryName(second));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void IsOpenableAttachment_accepts_a_real_file_written_into_a_per_open_directory()
    {
        var dir = AttachmentTempPaths.CreateOpenDirectory();
        try
        {
            var path = Path.Join(dir, "brand.jpg");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.True(AttachmentTempPaths.IsOpenableAttachment(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsOpenableAttachment_refuses_a_file_outside_the_root()
    {
        var path = Path.Join(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        try
        {
            Assert.False(AttachmentTempPaths.IsOpenableAttachment(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsOpenableAttachment_refuses_a_traversal_that_only_looks_like_it_is_inside()
    {
        var outside = Path.Join(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });
        try
        {
            var traversal = Path.Join(AttachmentTempPaths.Root, "..", Path.GetFileName(outside));

            Assert.False(AttachmentTempPaths.IsOpenableAttachment(traversal));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    // A sibling directory whose name merely starts with the root's — the prefix test must not
    // accept "…/lagebuch-evil/x.jpg" as living under "…/lagebuch".
    [Fact]
    public void IsOpenableAttachment_refuses_a_sibling_directory_with_the_roots_name_as_a_prefix()
    {
        var sibling = AttachmentTempPaths.Root + "-evil";
        Directory.CreateDirectory(sibling);
        try
        {
            var path = Path.Join(sibling, "brand.jpg");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.False(AttachmentTempPaths.IsOpenableAttachment(path));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void IsOpenableAttachment_refuses_a_missing_file_a_directory_and_the_root_itself()
    {
        var dir = AttachmentTempPaths.CreateOpenDirectory();
        try
        {
            Assert.False(AttachmentTempPaths.IsOpenableAttachment(Path.Join(dir, "nope.jpg")));
            Assert.False(AttachmentTempPaths.IsOpenableAttachment(dir));
            Assert.False(AttachmentTempPaths.IsOpenableAttachment(AttachmentTempPaths.Root));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Inside the root, but not a regular file: following it would launch whatever it points at.
    [Fact]
    public void IsOpenableAttachment_refuses_a_symlink_planted_inside_the_root()
    {
        var outside = Path.Join(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });
        var dir = AttachmentTempPaths.CreateOpenDirectory();
        try
        {
            var link = Path.Join(dir, "brand.jpg");
            File.CreateSymbolicLink(link, outside);

            Assert.False(AttachmentTempPaths.IsOpenableAttachment(link));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            File.Delete(outside);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsOpenableAttachment_refuses_a_blank_path(string? blank)
    {
        Assert.False(AttachmentTempPaths.IsOpenableAttachment(blank));
    }

    // Location alone is not enough: the OS launches by extension, so a legacy or hostile row that
    // names an executable type must not reach the launcher even from inside our own directory.
    [Theory]
    [InlineData("Lageplan.hta")]
    [InlineData("Lageplan.desktop")]
    [InlineData("Lageplan.exe")]
    [InlineData("Lageplan")]
    public void IsOpenableAttachment_refuses_an_extension_that_is_not_an_allowed_attachment_type(string name)
    {
        var dir = AttachmentTempPaths.CreateOpenDirectory();
        try
        {
            var path = Path.Join(dir, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.False(AttachmentTempPaths.IsOpenableAttachment(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("brand.jpg")]
    [InlineData("brand.JPEG")]
    [InlineData("brand.png")]
    [InlineData("brand.gif")]
    [InlineData("brand.webp")]
    [InlineData("bericht.pdf")]
    public void IsOpenableAttachment_accepts_every_allowed_attachment_extension(string name)
    {
        var dir = AttachmentTempPaths.CreateOpenDirectory();
        try
        {
            var path = Path.Join(dir, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.True(AttachmentTempPaths.IsOpenableAttachment(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Path.GetFullPath throws on a NUL byte (and on an over-long path) — a gate must answer "no",
    // not blow up in the caller's face.
    [Fact]
    public void IsOpenableAttachment_refuses_a_path_the_platform_cannot_even_resolve()
    {
        Assert.False(AttachmentTempPaths.IsOpenableAttachment("\0evil.png"));
    }

    // Resolved against the working directory, so it lands nowhere near the root.
    [Fact]
    public void IsOpenableAttachment_refuses_a_relative_path()
    {
        Assert.False(AttachmentTempPaths.IsOpenableAttachment("relative/but/elsewhere.png"));
    }

    // The sweep tests run against a private root: xUnit runs test classes in parallel, and a sweep
    // of the real Root would delete directories other tests are still using.
    private static string NewSweepRoot() =>
        Path.Join(Path.GetTempPath(), $"lagebuch-sweep-test-{Guid.NewGuid():N}");

    private static string NewOpenDirectory(string root)
    {
        var directory = Path.Join(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Join(directory, "brand.jpg"), new byte[] { 1, 2, 3 });
        return directory;
    }

    [Fact]
    public void SweepOpenDirectories_removes_every_open_directory_with_its_files()
    {
        var root = NewSweepRoot();
        try
        {
            var first = NewOpenDirectory(root);
            var second = NewOpenDirectory(root);

            AttachmentTempPaths.SweepOpenDirectories(root);

            Assert.False(Directory.Exists(first));
            Assert.False(Directory.Exists(second));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SweepOpenDirectories_does_nothing_when_the_root_does_not_exist()
    {
        var root = NewSweepRoot();

        AttachmentTempPaths.SweepOpenDirectories(root);

        Assert.False(Directory.Exists(root));
    }

    // Only names CreateOpenDirectory produces: the sweep deletes recursively, so it must not guess.
    [Fact]
    public void SweepOpenDirectories_leaves_an_entry_it_did_not_create_alone()
    {
        var root = NewSweepRoot();
        try
        {
            var foreign = Path.Join(root, "not-a-guid");
            Directory.CreateDirectory(foreign);

            AttachmentTempPaths.SweepOpenDirectories(root);

            Assert.True(Directory.Exists(foreign));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // On Windows a viewer holding the copy open makes the directory undeletable; on Unix a
    // read-only subdirectory does the same. Either way the sweep skips it and carries on.
    [Fact]
    public void SweepOpenDirectories_skips_a_directory_it_cannot_delete_and_removes_the_others()
    {
        var root = NewSweepRoot();
        var locked = NewOpenDirectory(root);
        var free = NewOpenDirectory(root);
        var inner = Path.Join(locked, "inner");
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(inner);
                File.WriteAllBytes(Path.Join(inner, "lageplan.pdf"), new byte[] { 1, 2, 3 });
                File.SetUnixFileMode(inner, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            // A block, not a `using var`: the lock has to be gone before the finally deletes the root.
            using (OperatingSystem.IsWindows()
                ? new FileStream(Path.Join(locked, "brand.jpg"), FileMode.Open, FileAccess.Read, FileShare.None)
                : null)
            {
                AttachmentTempPaths.SweepOpenDirectories(root);

                Assert.False(Directory.Exists(free));

                // Root ignores the permission bits and deletes it anyway; there is nothing to lock then.
                if (OperatingSystem.IsWindows() || Environment.UserName != "root")
                {
                    Assert.True(Directory.Exists(locked));
                }
            }
        }
        finally
        {
            if (!OperatingSystem.IsWindows() && Directory.Exists(inner))
            {
                File.SetUnixFileMode(inner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    // A link planted in the root goes, but the recursive delete must not follow it outward.
    [Fact]
    public void SweepOpenDirectories_removes_a_planted_directory_link_without_touching_its_target()
    {
        var root = NewSweepRoot();
        var outside = Path.Join(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var precious = Path.Join(outside, "einsatz.pdf");
        File.WriteAllBytes(precious, new byte[] { 1, 2, 3 });
        try
        {
            Directory.CreateDirectory(root);
            var link = Path.Join(root, Guid.NewGuid().ToString("N"));
            Directory.CreateSymbolicLink(link, outside);

            AttachmentTempPaths.SweepOpenDirectories(root);

            Assert.False(Path.Exists(link));
            Assert.True(File.Exists(precious));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }
}
