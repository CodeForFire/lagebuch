using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class AttachmentTempPathsTests
{
    [Fact]
    public void Root_is_a_lagebuch_folder_under_the_system_temp_directory()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(), "lagebuch"), AttachmentTempPaths.Root);
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
            var path = Path.Combine(dir, "brand.jpg");
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
        var path = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.jpg");
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
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });
        try
        {
            var traversal = Path.Combine(AttachmentTempPaths.Root, "..", Path.GetFileName(outside));

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
            var path = Path.Combine(sibling, "brand.jpg");
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
            Assert.False(AttachmentTempPaths.IsOpenableAttachment(Path.Combine(dir, "nope.jpg")));
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
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });
        var dir = AttachmentTempPaths.CreateOpenDirectory();
        try
        {
            var link = Path.Combine(dir, "brand.jpg");
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
            var path = Path.Combine(dir, name);
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
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.True(AttachmentTempPaths.IsOpenableAttachment(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Path.GetFullPath throws on a NUL byte or an over-long path — a gate must answer "no", not
    // blow up in the caller's face.
    [Theory]
    [InlineData("\0evil.png")]
    [InlineData("relative/but/unresolvable.png")]
    public void IsOpenableAttachment_refuses_a_path_the_platform_cannot_even_resolve(string path)
    {
        Assert.False(AttachmentTempPaths.IsOpenableAttachment(path));
    }
}
