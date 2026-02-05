using Microsoft.JSInterop;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Web.Services;

public sealed class KeyboardCaptureService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IBufferedKeyReader _keyReader;
    private readonly ILogger<KeyboardCaptureService> _logger;
    private IJSObjectReference? _module;
    private DotNetObjectReference<KeyboardCaptureService>? _dotNetRef;

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
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Keyboard capture initialized");
    }

    [JSInvokable]
    public void OnKeyPressed(KeyInfoDto keyInfo)
    {
        var consoleKeyInfo = keyInfo.ToConsoleKeyInfo();
        if (consoleKeyInfo.IsEmpty) return;

        _keyReader.EnqueueKey(consoleKeyInfo);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Key pressed: {Key}", consoleKeyInfo.Key);
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
        }
        _dotNetRef?.Dispose();
    }
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
