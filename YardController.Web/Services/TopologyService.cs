using YardController.Web.Models;

namespace YardController.Web.Services;

public class TopologyService : IDisposable
{
    private readonly TopologyParser _parser = new();
    private readonly string _filePath;
    private readonly FileSystemWatcher _watcher;
    private readonly ILogger<TopologyService> _logger;

    private YardTopology _currentTopology = YardTopology.Empty;
    private DateTime _lastReload = DateTime.MinValue;

    public event Action? TopologyChanged;

    public YardTopology CurrentTopology => _currentTopology;

    public TopologyService(IConfiguration configuration, ILogger<TopologyService> logger)
    {
        _logger = logger;

        // Get path from configuration or use default
        _filePath = configuration["TopologyFile"]
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "YardController.App", "Data", "Topology.txt");

        _filePath = Path.GetFullPath(_filePath);
        _logger.LogInformation("Topology file path: {FilePath}", _filePath);

        // Set up file watcher
        var directory = Path.GetDirectoryName(_filePath)!;
        var fileName = Path.GetFileName(_filePath);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
    }

    public async Task InitializeAsync()
    {
        await ReloadAsync();
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce - ignore rapid successive changes
        if ((DateTime.Now - _lastReload).TotalMilliseconds < 500)
        {
            return;
        }

        _logger.LogInformation("Topology file changed, reloading...");

        // Small delay to ensure file is fully written
        await Task.Delay(100);

        await ReloadAsync();
        TopologyChanged?.Invoke();
    }

    private async Task ReloadAsync()
    {
        try
        {
            _currentTopology = await _parser.ParseFileAsync(_filePath);
            _lastReload = DateTime.Now;

            _logger.LogInformation(
                "Loaded topology: {Nodes} nodes, {Links} links, {Points} points, {Signals} signals, {Labels} labels, {Gaps} gaps",
                _currentTopology.Graph.Nodes.Count,
                _currentTopology.Graph.Links.Count,
                _currentTopology.Points.Count,
                _currentTopology.Signals.Count,
                _currentTopology.Labels.Count,
                _currentTopology.Gaps.Count);

            // Debug: log each point
            foreach (var point in _currentTopology.Points)
            {
                _logger.LogInformation(
                    "Point '{Label}': switch={Switch}, diverging={Diverging}, direction={Direction}",
                    point.Label, point.SwitchPoint, point.DivergingEnd, point.Direction);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load topology from {FilePath}", _filePath);
        }
    }

    public void Dispose()
    {
        _watcher.Changed -= OnFileChanged;
        _watcher.Dispose();
    }
}
