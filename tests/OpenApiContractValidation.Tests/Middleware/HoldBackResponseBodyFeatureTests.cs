using System.IO;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using OpenApiContractValidation.Middleware;
using Xunit;

namespace OpenApiContractValidation.Tests.Middleware;

public class HoldBackResponseBodyFeatureTests
{
    [Fact]
    public async Task WritesToStream_DoNotReachInner_UntilCommit()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(
            fake,
            maxBufferSizeBytes: 1024 * 1024
        );

        byte[] hello = Encoding.UTF8.GetBytes("hello");
        feature.Stream.Write(hello, 0, hello.Length);

        Assert.Equal(0, fake.Inner.Length);

        await feature.CommitAsync(default);

        Assert.Equal("hello", Encoding.UTF8.GetString(fake.Inner.ToArray()));
    }

    [Fact]
    public async Task WritesToWriter_AreBuffered_AndCommitReplaysThem()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(
            fake,
            maxBufferSizeBytes: 1024 * 1024
        );

        byte[] payload = Encoding.UTF8.GetBytes("{\"id\":1}");
        await feature.Writer.WriteAsync(payload);

        Assert.Equal(0, fake.Inner.Length);

        await feature.CommitAsync(default);

        Assert.Equal("{\"id\":1}", Encoding.UTF8.GetString(fake.Inner.ToArray()));
    }

    [Fact]
    public async Task StartAsync_IsNoOp_WhileBuffering()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(
            fake,
            maxBufferSizeBytes: 1024 * 1024
        );

        await feature.StartAsync();

        Assert.False(fake.Started);
    }

    [Fact]
    public async Task SendFileAsync_CopiesIntoBuffer_NotInner()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(
            fake,
            maxBufferSizeBytes: 1024 * 1024
        );

        byte[] content = Encoding.UTF8.GetBytes("file-body-content");
        string tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, content);

        try
        {
            await feature.SendFileAsync(tmp, 0, count: null, default);

            Assert.Equal(0, fake.Inner.Length);
            Assert.Equal(content, feature.GetBufferedBytes());

            await feature.CommitAsync(default);

            Assert.Equal(content, fake.Inner.ToArray());
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task DisableBuffering_SwitchesToPassThrough()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(
            fake,
            maxBufferSizeBytes: 1024 * 1024
        );

        feature.DisableBuffering();

        Assert.True(feature.BufferingDisabled);
        Assert.True(fake.BufferingDisabled);

        byte[] more = Encoding.UTF8.GetBytes("passthrough");
        feature.Stream.Write(more, 0, more.Length);

        Assert.Equal(more.Length, fake.Inner.Length);
        Assert.Equal("passthrough", Encoding.UTF8.GetString(fake.Inner.ToArray()));
    }

    [Fact]
    public async Task ExceedingCap_Throws()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 8);

        byte[] big = new byte[100];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            feature.Stream.Write(big, 0, big.Length)
        );

        Assert.Contains("response exceeded validation buffer", ex.Message);
    }

    /// <summary>
    /// A test double for <see cref="IHttpResponseBodyFeature"/> backed by a public
    /// <see cref="MemoryStream"/> so tests can assert exactly what reached the host.
    /// </summary>
    private sealed class FakeResponseBodyFeature : IHttpResponseBodyFeature
    {
        private readonly PipeWriter _writer;

        public FakeResponseBodyFeature()
        {
            Inner = new MemoryStream();
            _writer = PipeWriter.Create(Inner, new StreamPipeWriterOptions(leaveOpen: true));
        }

        public MemoryStream Inner { get; }

        public bool Started { get; private set; }

        public bool Completed { get; private set; }

        public bool BufferingDisabled { get; private set; }

        public Stream Stream => Inner;

        public PipeWriter Writer => _writer;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task CompleteAsync()
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public Task SendFileAsync(
            string path,
            long offset,
            long? count,
            CancellationToken cancellationToken = default
        )
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (offset > 0)
            {
                fs.Seek(offset, SeekOrigin.Begin);
            }

            fs.CopyTo(Inner);
            return Task.CompletedTask;
        }

        public void DisableBuffering() => BufferingDisabled = true;
    }
}
