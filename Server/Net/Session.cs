using System.Buffers;
using Spire.Protocol;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace Spire.Net;

public class Session(Stream stream)
{
    private readonly PipeReader _reader = PipeReader.Create(stream);
    private readonly PipeWriter _writer = PipeWriter.Create(stream);
    private readonly Channel<byte[]> _sendChannel = 
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cancellation = new();

    private Task? _receiveLoopTask;
    private Task? _sendLoopTask;

    public void Open()
    {
        _receiveLoopTask = ReceiveLoopAsync();
        _sendLoopTask = SendLoopAsync();
    }
    
    public async Task CloseAsync()
    {
        if (!_cancellation.IsCancellationRequested)
            await _cancellation.CancelAsync();
        
        _sendChannel.Writer.TryComplete();
        
        await _reader.CompleteAsync();
        await _writer.CompleteAsync();
        _cancellation.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            var headerBuffer = (await _reader.ReadAtLeastAsync(
                ProtocolHeader.HeaderSize, _cancellation.Token)).Buffer;
            
            var (category, length) = ProtocolHeader.Read(headerBuffer.ToArray());
            _reader.AdvanceTo(headerBuffer.GetPosition(ProtocolHeader.HeaderSize));

            if (length == 0)
                break;
            
            var bodyBuffer = (await _reader.ReadAtLeastAsync(
                length, _cancellation.Token)).Buffer;
            // TODO: Send message
            _reader.AdvanceTo(bodyBuffer.GetPosition(length));
        }
    }

    private async Task SendLoopAsync()
    {
        await foreach (var buffer in _sendChannel.Reader.ReadAllAsync(_cancellation.Token))
        {
            await _writer.WriteAsync(buffer, _cancellation.Token);
            // await _writer.FlushAsync(_cancellation.Token);
            
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task SendAsync(byte[] buffer)
    {
        await _sendChannel.Writer.WriteAsync(buffer, _cancellation.Token);
    }
}