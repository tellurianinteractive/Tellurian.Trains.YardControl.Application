using System.Globalization;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Tellurian.Trains.YardController.Keys;
using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;
using Tellurian.Trains.YardController.Panel.Hardware;
using Tellurian.Trains.YardController.Panel.Rendering;

// The key faces of the operator panel: renders them to files to be looked at, reports which face
// a machine will letter them in, and drives a Stream Deck Module directly so a panel can be
// proved before any of the yard is wired to it. Run with --help for the options.

var arguments = CommandLine.Parse(args);

if (arguments.Flag("help") || args.Contains("-h")) { WriteUsage(); return 0; }
if (arguments.Flag("fonts")) return ReportFonts();
if (arguments.Flag("panel")) return await DrivePanelAsync(arguments).ConfigureAwait(false);
return RenderToFiles(arguments);

static int ReportFonts()
{
    // Which face a machine will actually letter keys in is worth being able to ask: a silent
    // fallback to a normal-width face changes how much of a word fits a key, and the fallback
    // cannot be spotted by name because a narrow cut reports the family it belongs to.
    // "Ablenkung" is the longest label in the set, so it is what gets measured.
    const string Sample = "Ablenkung";
    var options = new KeyFaceOptions();
    var nominal = options.Size * options.LabelHeight;
    var usable = options.Size * (1f - (2f * options.SideMargin));
    var installed = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);

    Console.WriteLine($"{installed.Count} families visible to Skia.");
    Console.WriteLine($"'{Sample}' at {Px(nominal)} px, against {Px(usable)} px of usable key width.");
    Console.WriteLine();
    Console.WriteLine($"{"Family",-26}{"Present",9}{"Normal",9}{"Condensed",11}");
    Console.WriteLine(new string('-', 55));

    foreach (var family in options.FontFamilies)
    {
        var present = installed.Contains(family);
        var widths = new List<string>();
        foreach (var width in new[] { SKFontStyleWidth.Normal, options.FontWidth })
        {
            if (!present) { widths.Add("-"); continue; }
            using var style = new SKFontStyle(options.FontWeight, width, SKFontStyleSlant.Upright);
            using var face = SKTypeface.FromFamilyName(family, style);
            using var font = new SKFont(face, nominal);
            var measured = font.MeasureText(Sample);
            widths.Add($"{Px(measured)}{(measured <= usable ? "" : "!")}");
        }
        Console.WriteLine($"{family,-26}{(present ? "yes" : "no"),9}{widths[0],9}{widths[1],11}");
    }

    Console.WriteLine(new string('-', 55));
    Console.WriteLine("! means the word overruns the key and would be shrunk to fit.");
    Console.WriteLine();
    using var faces = new PanelKeyFaces(options);
    Console.WriteLine($"Labels would be set in: {faces.FontFamily}");
    return 0;
}

static int RenderToFiles(CommandLine arguments)
{
    var language = arguments.Value("lang");
    var outputFolder = Path.GetFullPath(arguments.Value("out")
        ?? Path.Combine("Steam Deck Keys", "rendered", language ?? "plain"));

    var options = new KeyFaceOptions
    {
        Size = arguments.Number("size") ?? 96,
        JpegQuality = arguments.Number("quality") ?? 92,
        Rotate180 = arguments.Flag("rotate"),
        Png = arguments.Flag("png"),
        AlternateHairline = arguments.Flag("alt"),
    };

    using var faces = new PanelKeyFaces(options);
    if (language is not null && !faces.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Language '{language}' is not in the label table. Known: {string.Join(", ", faces.Languages)}");
        return 1;
    }

    Directory.CreateDirectory(outputFolder);
    Console.WriteLine($"Out     {outputFolder}");
    Console.WriteLine($"Size    {options.Size} px {(options.Png ? "PNG" : $"JPEG q{options.JpegQuality}")}"
        + $"{(options.Rotate180 ? ", turned for the device" : "")}{(options.AlternateHairline ? ", alternate hairline" : "")}");
    Console.WriteLine($"Labels  {(language is null ? "none - icons alone" : language)}");
    Console.WriteLine($"Face    {faces.FontFamily}");
    Console.WriteLine();
    Console.WriteLine($"{"Icon",-26}{"Bytes",7}  {"Label",-14}{"Height",7}");
    Console.WriteLine(new string('-', 58));

    var extension = options.Png ? ".png" : ".jpg";
    var tight = new List<KeyFaceResult>();
    var unreadable = new List<KeyFaceResult>();
    var total = 0;
    var count = 0;

    foreach (var command in Enum.GetValues<PanelCommand>().Where(c => c != PanelCommand.None).OrderBy(c => c.IconName, StringComparer.Ordinal))
    {
        var face = faces.For(command, language);
        var bytes = faces.Encode(face);
        File.WriteAllBytes(Path.Combine(outputFolder, command.IconName + extension), bytes.Span);
        total += bytes.Length;
        count++;

        if (face.Label is not null && !face.Fits) unreadable.Add(face);
        else if (face.WasShrunk) tight.Add(face);

        var height = face.Label is null ? "" : Px(face.LabelPixels);
        var mark = face.Label is null ? " " : face.Fits ? (face.WasShrunk ? "~" : " ") : "!";
        Console.WriteLine($"{command.IconName,-26}{bytes.Length,7}  {face.Label ?? "",-14}{height,6}{mark}");
    }

    // The numeric pad as well: those keys carry no icon, but they are still faces the panel has
    // to paint, and their symbols are the ones most likely to be missing from a face.
    foreach (var key in NumericPad.Keys)
    {
        var face = faces.For(key, language);
        var bytes = faces.Encode(face);
        File.WriteAllBytes(Path.Combine(outputFolder, NumericPad.GlyphName(key) + extension), bytes.Span);
        total += bytes.Length;
        count++;
        Console.WriteLine($"{NumericPad.GlyphName(key),-26}{bytes.Length,7}  {face.Label ?? "",-14}");
    }

    Console.WriteLine(new string('-', 58));
    Console.WriteLine($"{count} keys, {total} bytes, {total / (double)count:F0} bytes average.");

    if (tight.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Shrunk to fit, still readable:");
        foreach (var r in tight) Console.WriteLine($"  {r.Name,-26}{r.Label,-14}{Px(r.LabelPixels),6} px");
    }

    if (unreadable.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Below the {options.MinimumLabelHeight * options.Size:F1} px floor - these want a shorter word:");
        foreach (var r in unreadable) Console.WriteLine($"  {r.Name,-26}{r.Label,-14}{Px(r.LabelPixels),6} px");
    }

    return unreadable.Count > 0 ? 2 : 0;
}

/// <summary>
/// Drives a real module. Everything about a panel that cannot be settled without one in front of
/// you is settled here: whether it is found at all, which key the hardware calls the one at the
/// operator's top left, and which way up the pictures come out.
/// </summary>
static async Task<int> DrivePanelAsync(CommandLine arguments)
{
    var language = arguments.Value("lang");
    var options = new StreamDeckPanelOptions
    {
        Rotate180 = !arguments.Flag("no-rotate"),
        InvertKeys = !arguments.Flag("no-invert"),
        Brightness = (byte)(arguments.Number("brightness") ?? 80),
    };

    using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(c => c.SingleLine = true).SetMinimumLevel(LogLevel.Information));
    using var faces = new PanelKeyFaces();
    using var panel = new StreamDeckButtonPanel(options, loggerFactory.CreateLogger<StreamDeckButtonPanel>());

    Console.WriteLine($"Images turned:  {options.Rotate180}   (--no-rotate to leave them alone)");
    Console.WriteLine($"Keys inverted:  {options.InvertKeys}   (--no-invert to count from the other corner)");
    Console.WriteLine($"Numbers mode:   {arguments.Flag("numbers")}   (--numbers paints each key with its index)");
    Console.WriteLine();
    Console.WriteLine("Waiting for a Stream Deck Module. The Elgato Stream Deck application must not be running.");
    Console.WriteLine("Press keys on the panel; ctrl-C to stop.");
    Console.WriteLine();

    panel.KeyStateChanged += (index, isDown) =>
    {
        if (!isDown) return;
        var row = (index / 8) + 1;
        var column = (index % 8) + 1;
        var side = column <= 4 ? "command pad" : "numeric pad";
        var what = column <= 4
            ? ButtonLayout.Default[ButtonSet.Main, ((row - 1) * 4) + column - 1].IconName
            : NumericPad.At(((row - 1) * 4) + column - 5).ToString();
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  key {index,2}  row {row} column {column}  {side,-12} {(what.Length == 0 ? "(blank)" : what)}");
    };

    // Reported with Console rather than the logger: the logger's console sink hands lines to a
    // background queue, and a run ended by a kill loses whatever is still in it — which is exactly
    // the run you most want the record of.
    var arrivals = 0;
    var departures = 0;

    // Painting happens on the polling loop alone. The arrival event arrives on the panel's own
    // timer thread, and a handler that painted there would race the loop — both would see a
    // connected panel that had not been painted yet, and both would paint it.
    var wantsPaint = false;
    panel.ConnectionChanged += connected =>
    {
        if (!connected)
        {
            departures++;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  panel gone      (arrivals {arrivals}, departures {departures})");
            return;
        }
        arrivals++;
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  panel arrived   (arrivals {arrivals}, departures {departures})");
        Volatile.Write(ref wantsPaint, true);
    };

    // A run can bound itself, so that a session driven from a script ends by its own shutdown path
    // and prints what it saw. Killing it from outside loses exactly the summary worth having.
    using var stopping = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };
    if (arguments.Number("seconds") is { } seconds and > 0)
    {
        stopping.CancelAfter(TimeSpan.FromSeconds(seconds));
        Console.WriteLine($"Stopping by itself after {seconds} s.");
    }

    try
    {
        while (!stopping.IsCancellationRequested)
        {
            if (Volatile.Read(ref wantsPaint) && panel.IsConnected)
            {
                Volatile.Write(ref wantsPaint, false);
                Paint(panel, faces, language, arguments.Flag("numbers"));
            }
            await Task.Delay(200, stopping.Token).ConfigureAwait(false);
        }
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
    }

    panel.Clear();
    Console.WriteLine();
    Console.WriteLine($"Link: {arrivals} arrival(s), {departures} departure(s). More than one arrival means the module is dropping off the bus.");
    return 0;
}

/// <summary>
/// Paints the whole panel: the default command pad on the left, the numeric pad on the right.
/// In numbers mode every key carries its own index instead, which is how the hardware's numbering
/// is read off the panel rather than guessed at.
/// </summary>
static void Paint(StreamDeckButtonPanel panel, PanelKeyFaces faces, string? language, bool numbers)
{
    for (var row = 0; row < 4; row++)
    {
        for (var column = 0; column < 8; column++)
        {
            var index = (row * 8) + column;
            var face = numbers
                ? faces.For((char)('0' + (index % 10)), language)
                : column < 4
                    ? faces.For(ButtonLayout.Default[ButtonSet.Main, (row * 4) + column], language)
                    : faces.For(NumericPad.At((row * 4) + column - 4), language);
            panel.SetKeyImage(index, face.Bgr24.Span);
        }
    }
    Console.WriteLine(numbers
        ? "Painted every key with its index. Read the top left key: it should show 0."
        : "Painted the default layout. The command pad should be on the left.");
}

/// <summary>One decimal, always with a point, so a measurement reads the same on every machine.</summary>
static string Px(float value) => value.ToString("F1", CultureInfo.InvariantCulture);

static void WriteUsage() => Console.WriteLine("""
    The key faces of the operator panel.

    Rendering to files (the default):
      --out <folder>      where to write            (default: Steam Deck Keys/rendered/<lang>)
      --lang <code>       label language, e.g. sv   (default: none, icons alone)
      --size <pixels>     key edge                  (default: 96)
      --quality <1-100>   JPEG quality              (default: 92)
      --rotate            turn a half turn
      --png               write PNG instead of JPEG, for looking at
      --alt               add the alternate-set white hairline

    Reporting:
      --fonts             which face this machine will letter keys in

    Driving a Stream Deck Module:
      --panel             open the module and report every key press
      --numbers           paint each key with its own index, to read the numbering off
      --lang <code>       label language for the painted keys
      --no-rotate         leave the pictures the way up they were drawn
      --no-invert         count keys from the near corner instead of the far one
      --brightness <n>    backlight, 0 to 100       (default: 80)
      --seconds <n>       stop by itself after n seconds, printing what it saw

    Exits 2 when a label had to be shrunk below the legibility floor.
    """);

namespace Tellurian.Trains.YardController.Keys
{
    /// <summary>
    /// The little that is needed to read <c>--name value</c> and <c>--flag</c> off a command line.
    /// </summary>
    internal sealed class CommandLine
    {
        private readonly Dictionary<string, string?> _values;

        private CommandLine(Dictionary<string, string?> values) => _values = values;

        public static CommandLine Parse(string[] args)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
                var name = args[i][2..];
                var next = i + 1 < args.Length ? args[i + 1] : null;
                if (next is not null && !next.StartsWith("--", StringComparison.Ordinal))
                {
                    values[name] = next;
                    i++;
                }
                else
                {
                    values[name] = null;
                }
            }
            return new CommandLine(values);
        }

        public string? Value(string name) => _values.TryGetValue(name, out var value) ? value : null;

        public bool Flag(string name) => _values.ContainsKey(name);

        public int? Number(string name) =>
            int.TryParse(Value(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
