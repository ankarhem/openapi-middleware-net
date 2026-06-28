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

    [Fact]
    public void Constructor_NullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HoldBackResponseBodyFeature(null!, maxBufferSizeBytes: 1024)
        );
    }

    [Fact]
    public void Constructor_ZeroMaxBuffer_ThrowsArgumentOutOfRangeException()
    {
        var fake = new FakeResponseBodyFeature();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 0)
        );
    }

    [Fact]
    public void Constructor_NegativeMaxBuffer_ThrowsArgumentOutOfRangeException()
    {
        var fake = new FakeResponseBodyFeature();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: -1)
        );
    }

    [Fact]
    public async Task BufferedLength_ReturnsWrittenByteCount()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] data = Encoding.UTF8.GetBytes("hello world");
        feature.Stream.Write(data, 0, data.Length);

        Assert.Equal(data.Length, feature.BufferedLength);
    }

    [Fact]
    public async Task Writer_ReturnsInnerWriter_AfterDisableBuffering()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        feature.DisableBuffering();

        Assert.Same(fake.Writer, feature.Writer);

        byte[] data = Encoding.UTF8.GetBytes("direct");
        await feature.Writer.WriteAsync(data);
        await feature.Writer.FlushAsync();

        Assert.Equal("direct", Encoding.UTF8.GetString(fake.Inner.ToArray()));
    }

    [Fact]
    public async Task Writer_ReturnsSameInstance_OnRepeatedAccess()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        PipeWriter first = feature.Writer;
        PipeWriter second = feature.Writer;

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetBufferedBytes_FlushesWriter_BeforeReturning()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] data = Encoding.UTF8.GetBytes("buffered-via-writer");
        await feature.Writer.WriteAsync(data);

        Assert.Equal(data, feature.GetBufferedBytes());
    }

    [Fact]
    public async Task StartAsync_DelegatesToInner_AfterDisableBuffering()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        feature.DisableBuffering();
        await feature.StartAsync();

        Assert.True(fake.Started);
    }

    [Fact]
    public async Task CompleteAsync_IsNoOp_WhileBuffering()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        await feature.CompleteAsync();

        Assert.False(fake.Completed);
    }

    [Fact]
    public async Task CompleteAsync_DelegatesToInner_AfterDisableBuffering()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        feature.DisableBuffering();
        await feature.CompleteAsync();

        Assert.True(fake.Completed);
    }

    [Fact]
    public async Task SendFileAsync_DelegatesToInner_AfterDisableBuffering()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] content = Encoding.UTF8.GetBytes("sendfile-passthrough");
        string tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, content);

        try
        {
            feature.DisableBuffering();
            await feature.SendFileAsync(tmp, 0, count: null, default);

            Assert.Equal(content, fake.Inner.ToArray());
            Assert.Equal(0, feature.BufferedLength);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task SendFileAsync_WithOffset_SkipsLeadingBytes()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] content = Encoding.UTF8.GetBytes("ABCDEFGH");
        string tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, content);

        try
        {
            await feature.SendFileAsync(tmp, offset: 3, count: null, default);

            Assert.Equal("DEFGH", Encoding.UTF8.GetString(feature.GetBufferedBytes()));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task SendFileAsync_WithCount_LimitsBytesBuffered()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] content = Encoding.UTF8.GetBytes("ABCDEFGH");
        string tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, content);

        try
        {
            await feature.SendFileAsync(tmp, 0, count: 4, default);

            Assert.Equal("ABCD", Encoding.UTF8.GetString(feature.GetBufferedBytes()));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task SendFileAsync_WithCountLargerThanFile_ReadsToEnd()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] content = Encoding.UTF8.GetBytes("ABCDEFGH");
        string tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, content);

        try
        {
            await feature.SendFileAsync(tmp, 0, count: 100, default);

            Assert.Equal("ABCDEFGH", Encoding.UTF8.GetString(feature.GetBufferedBytes()));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task DisableBuffering_CalledTwice_IsIdempotent()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        feature.DisableBuffering();
        feature.DisableBuffering();

        Assert.True(feature.BufferingDisabled);
        Assert.True(fake.BufferingDisabled);
    }

    [Fact]
    public async Task DisableBuffering_CopiesAlreadyBufferedBytesToInner()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] hello = Encoding.UTF8.GetBytes("hello");
        feature.Stream.Write(hello, 0, hello.Length);

        Assert.Equal(0, fake.Inner.Length);

        feature.DisableBuffering();

        Assert.Equal("hello", Encoding.UTF8.GetString(fake.Inner.ToArray()));
        Assert.True(feature.BufferingDisabled);
        Assert.True(fake.BufferingDisabled);
    }

    [Fact]
    public async Task CommitAsync_CalledTwice_DoesNotDoubleWrite()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] hello = Encoding.UTF8.GetBytes("hello");
        feature.Stream.Write(hello, 0, hello.Length);

        await feature.CommitAsync(default);
        await feature.CommitAsync(default);

        Assert.Equal("hello", Encoding.UTF8.GetString(fake.Inner.ToArray()));
        Assert.True(fake.Completed);
    }

    [Fact]
    public async Task CommitAsync_InPassThroughMode_FlushesAndCompletesInner()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        feature.DisableBuffering();
        await feature.CommitAsync(default);

        Assert.True(fake.Completed);
        Assert.True(feature.Committed);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_ReturnsEarly()
    {
        var fake = new FakeResponseBodyFeature();
        var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        await feature.Writer.WriteAsync(Encoding.UTF8.GetBytes("x"));

        await feature.DisposeAsync();
        await feature.DisposeAsync();
    }

    [Fact]
    public async Task WritingEmptyBytes_IsNoOp()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 8);

        feature.Stream.Write(Array.Empty<byte>(), 0, 0);

        Assert.Equal(0, feature.BufferedLength);
    }

    [Fact]
    public async Task ExceedingCap_ViaWriterPath_Throws()
    {
        var fake = new FakeResponseBodyFeature();
        var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 8);

        byte[] big = new byte[100];

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            feature.Writer.WriteAsync(big).AsTask()
        );

        Assert.Contains("response exceeded validation buffer", ex.Message);
    }

    [Fact]
    public async Task CapStream_Capabilities_AllTrue()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        Assert.True(feature.Stream.CanRead);
        Assert.True(feature.Stream.CanSeek);
        Assert.True(feature.Stream.CanWrite);
    }

    [Fact]
    public async Task CapStream_LengthAndPosition_TrackBuffer()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] data = Encoding.UTF8.GetBytes("hello");
        feature.Stream.Write(data, 0, data.Length);

        Assert.Equal(data.Length, feature.Stream.Length);
        Assert.Equal(data.Length, feature.Stream.Position);

        feature.Stream.Position = 0;
        Assert.Equal(0, feature.Stream.Position);
    }

    [Fact]
    public async Task CapStream_Read_ReturnsBufferedBytes()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] data = Encoding.UTF8.GetBytes("hello");
        feature.Stream.Write(data, 0, data.Length);
        feature.Stream.Position = 0;

        byte[] buffer = new byte[data.Length];
        int read = feature.Stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(data.Length, read);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public async Task CapStream_Seek_ChangesPosition()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        byte[] data = Encoding.UTF8.GetBytes("hello");
        feature.Stream.Write(data, 0, data.Length);

        long pos = feature.Stream.Seek(2, SeekOrigin.Begin);

        Assert.Equal(2, pos);
        Assert.Equal(2, feature.Stream.Position);
    }

    [Fact]
    public async Task CapStream_SetLength_ChangesBufferLength()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        feature.Stream.SetLength(5);

        Assert.Equal(5, feature.Stream.Length);
    }

    [Fact]
    public async Task CapStream_WriteReadOnlySpan_WritesToBuffer()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        ReadOnlySpan<byte> data = Encoding.UTF8.GetBytes("span-data");
        feature.Stream.Write(data);

        Assert.Equal("span-data", Encoding.UTF8.GetString(feature.GetBufferedBytes()));
    }

    [Fact]
    public async Task CapStream_Flush_And_FlushAsync_DoNotThrow()
    {
        var fake = new FakeResponseBodyFeature();
        await using var feature = new HoldBackResponseBodyFeature(fake, maxBufferSizeBytes: 1024);

        feature.Stream.Flush();
        await feature.Stream.FlushAsync();
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
