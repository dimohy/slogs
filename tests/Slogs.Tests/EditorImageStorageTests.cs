using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class EditorImageStorageTests
{
    private const string ValidPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=";

    [Fact]
    public async Task SaveAsyncWritesValidImageBytesUnchanged()
    {
        using var tempDirectory = new TempDirectory();
        var storage = new EditorImageStorage(new TestWebHostEnvironment(tempDirectory.Path));
        var imageBytes = Convert.FromBase64String(ValidPngBase64);

        using var source = new MemoryStream(imageBytes);
        var response = await storage.SaveAsync(
            source,
            "cover.png",
            "image/png",
            imageBytes.LongLength);

        var savedPath = Path.Combine(tempDirectory.Path, "uploads", Path.GetFileName(response.Url));
        var savedBytes = await File.ReadAllBytesAsync(savedPath);
        Assert.EndsWith(".png", response.Url);
        Assert.Equal(imageBytes, savedBytes);
    }

    [Fact]
    public async Task SaveAsyncRejectsMismatchedDeclaredImageType()
    {
        using var tempDirectory = new TempDirectory();
        var storage = new EditorImageStorage(new TestWebHostEnvironment(tempDirectory.Path));
        var imageBytes = Convert.FromBase64String(ValidPngBase64);

        using var source = new MemoryStream(imageBytes);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(
            source,
            "cover.jpg",
            "image/jpeg",
            imageBytes.LongLength));

        Assert.Contains("일치하지 않거나 손상", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(tempDirectory.Path, "uploads")));
    }

    [Fact]
    public async Task SaveAsyncRejectsTruncatedImageBytes()
    {
        using var tempDirectory = new TempDirectory();
        var storage = new EditorImageStorage(new TestWebHostEnvironment(tempDirectory.Path));
        var imageBytes = Convert.FromBase64String(ValidPngBase64);
        var truncatedBytes = imageBytes[..^12];

        using var source = new MemoryStream(truncatedBytes);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(
            source,
            "cover.png",
            "image/png",
            truncatedBytes.LongLength));

        Assert.Contains("일치하지 않거나 손상", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(tempDirectory.Path, "uploads")));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"slogs-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string webRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Slogs.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = webRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string WebRootPath { get; set; } = webRootPath;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
