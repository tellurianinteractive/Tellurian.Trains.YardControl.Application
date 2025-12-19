using System.Net;
using System.Net.Sockets;

namespace Tellurian.Trains.YardController;

public interface IKeyReader
{
    ConsoleKeyInfo ReadKey();
    bool KeyNotAvailable { get; }
}

public class ConsoleKeyReader : IKeyReader
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(true);
    public bool KeyNotAvailable { get => !Console.KeyAvailable; }
}

public sealed class UdpKeyReader : IKeyReader, IAsyncDisposable
{
    private readonly ILogger<UdpKeyReader> _logger;
    public UdpKeyReader(ILogger<UdpKeyReader> logger)
    {
        _logger = logger;
        if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("{Reader} initializing....", nameof(UdpKeyReader));
        _udpClient = new();
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 1100));
        _keyReaderTask = KeyReader(_cancellationTokenSource.Token);

        StartKeyReader();
    }
    private readonly UdpClient _udpClient;
    private readonly Queue<ConsoleKeyInfo> _keyQueue = new();
    private static readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _keyReaderTask;
    public ConsoleKeyInfo ReadKey()
    {
        if (KeyNotAvailable) return ConsoleKeyInfo.Empty;
        return _keyQueue.Dequeue();
    }
    public bool KeyNotAvailable
    {
        get
        {
            return _keyQueue.Count == 0;
        }
    }

    void StartKeyReader()
    {

        Task.Run(() => _keyReaderTask);
    }

    async Task KeyReader(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{Reader} started.", nameof(UdpKeyReader));
        }
        var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var receiveResult = await _udpClient.ReceiveAsync(cancellationToken);
                if (receiveResult.Buffer.Length > 0)
                {
                    var keyInfo = receiveResult.Buffer.Deserialize();
                    _keyQueue.Enqueue(keyInfo);
                }
                ;

            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{Reader} stopped.", nameof(UdpKeyReader));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _keyQueue.Clear();
        _cancellationTokenSource.Cancel();
        _udpClient.Close();
        _udpClient.Dispose();
        await Task.Delay(10);
        _keyReaderTask.Dispose();
    }
}

public class TestKeyReader() : IKeyReader
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    public void AddKey(char key) => _keys.Enqueue(new ConsoleKeyInfo(key, key.ConsoleKey, false, false, false));
    public ConsoleKeyInfo ReadKey() => _keys.Dequeue();
    public bool KeyNotAvailable { get => _keys.Count == 0; }
    public void Clear() => _keys.Clear();
}
