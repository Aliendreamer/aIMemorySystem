using System.Text;
using AiMemory.Storage;

namespace AiMemory.Tests;

public sealed class FileSystemBlobStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, "blobtest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveThenOpen_RoundTripsBytes()
    {
        var store = new FileSystemBlobStore(_root);
        var data = Encoding.UTF8.GetBytes("hello attachment");

        var info = await store.SaveAsync("Payments/issue-7/note.txt", new MemoryStream(data));

        Assert.Equal("Payments/issue-7/note.txt", info.VolumePath);
        Assert.Equal(data.Length, info.Size);
        await using var read = await store.OpenAsync(info.VolumePath);
        using var ms = new MemoryStream();
        await read.CopyToAsync(ms);
        Assert.Equal(data, ms.ToArray());
    }

    [Fact]
    public async Task Save_CreatesNestedDirectories()
    {
        var store = new FileSystemBlobStore(_root);

        var info = await store.SaveAsync("a/b/c/deep.bin", new MemoryStream([1, 2, 3]));

        Assert.Equal("a/b/c/deep.bin", info.VolumePath);
        Assert.True(File.Exists(Path.Combine(_root, "a", "b", "c", "deep.bin")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    public async Task Save_RejectsPathTraversal(string path)
    {
        var store = new FileSystemBlobStore(_root);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(path, new MemoryStream([0])));
    }
}
