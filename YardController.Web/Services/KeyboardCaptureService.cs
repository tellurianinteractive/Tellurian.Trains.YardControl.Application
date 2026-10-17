using Microsoft.JSInterop;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed partial class KeyboardCaptureService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IBufferedKeyReader _keyReader;
    private readonly ILogger<KeyboardCaptureService> _logger;
    private IJSObjectReference? _module;
    private DotNetObjectReference<KeyboardCaptureService>? _dotNetRef;

    /// <summary>
    /// The station name this capture service is associated with.
    /// Set by the hosting page before initializing.
    /// </summary>
    public string StationName { get; set; } = "";

    public KeyboardCaptureService(IJSRuntime jsRuntime, IBufferedKeyReader keyReader, ILogger<KeyboardCaptureService> logger)
    {
        _jsRuntime = jsRuntime;
        _keyReader = keyReader;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
        _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/keyboardCapture.js");
        await _module.InvokeVoidAsync("initializeKeyboardCapture", _dotNetRef);
        LogKeyboardCaptureInitialized();
    }

    [JSInvokable]
    public void OnKeyPressed(KeyInfoDto keyInfo)
    {
        var consoleKeyInfo = keyInfo.ToConsoleKeyInfo();
        if (consoleKeyInfo.IsEmpty) return;

        _keyReader.EnqueueKey(consoleKeyInfo, StationName);
        LogKeyPressed(consoleKeyInfo.Key);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("disposeKeyboardCapture");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Ignore - circuit already disconnected
            }
            _module = null;
        }
        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyboard capture initialized")]
    private partial void LogKeyboardCaptureInitialized();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Key pressed: {Key}")]
    private partial void LogKeyPressed(ConsoleKey key);
}

public record KeyInfoDto(string KeyChar, int Key, int Modifiers)
{
    public ConsoleKeyInfo ToConsoleKeyInfo()
    {
        var consoleKey = (ConsoleKey)Key;
        var keyChar = string.IsNullOrEmpty(KeyChar) ? '\0' : KeyChar[0];
        var shift = (Modifiers & 1) != 0;
        var alt = (Modifiers & 2) != 0;
        var control = (Modifiers & 4) != 0;
        return new ConsoleKeyInfo(keyChar, consoleKey, shift, alt, control);
    }
}
