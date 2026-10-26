using dbsh.Core.Enums;
using dbsh.Core.ValueObjects;
using Spectre.Console;

namespace dbsh.CLI.Helpers;

public static class ConsoleHelper
{
    public static bool UiSuppressed { get; set; }

    public static void PrintBanner()
    {
        AnsiConsole.WriteLine();

        var brand = GradientMarkup("dbsh", Theme.Primary, Theme.Accent);
        var version = FileVersionInfo.GetVersionString();

        AnsiConsole.Write(new Rule().RuleStyle(Style.Parse(Theme.Primary)).LeftJustified());
        AnsiConsole.MarkupLine($"  {brand} [grey]v{Markup.Escape(version)}[/]");
        AnsiConsole.MarkupLine($"  [grey]database migrations for .NET[/]");
        AnsiConsole.Write(new Rule().RuleStyle(Style.Parse(Theme.Muted)).LeftJustified());

        var info = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(2))
            .AddColumn()
            .AddRow($"[bold {Theme.Primary}]providers[/]", $"[grey]PostgreSQL / SQL Server / MySQL / SQLite[/]");

        AnsiConsole.Write(new Panel(info)
            .Border(BoxBorder.Rounded)
            .BorderStyle(Style.Parse(Theme.Muted))
            .Header($" [{Theme.Primary}] system [/]", Justify.Left)
            .Padding(1, 0));
        AnsiConsole.WriteLine();
    }

    public static void PrintHeader(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold {Theme.Primary}]{Markup.Escape(title)}[/]")
            .RuleStyle(Style.Parse(Theme.Primary))
            .LeftJustified());
        AnsiConsole.WriteLine();
    }

    public static void PrintSuccess(string message) =>
        AnsiConsole.MarkupLine($"  {Theme.Check} [white]{Markup.Escape(message)}[/]");

    public static void PrintError(string message) =>
        AnsiConsole.MarkupLine($"  {Theme.Cross} [white]{Markup.Escape(message)}[/]");

    public static void PrintWarning(string message) =>
        AnsiConsole.MarkupLine($"  {Theme.Warn} [white]{Markup.Escape(message)}[/]");

    public static void PrintInfo(string message) =>
        AnsiConsole.MarkupLine($"  {Theme.InfoGlyph} [white]{Markup.Escape(message)}[/]");

    public static void PrintStep(string message) =>
        AnsiConsole.MarkupLine($"  {Theme.Step} [grey]{Markup.Escape(message)}[/]");

    public static void PrintDivider() =>
        AnsiConsole.Write(new Rule().RuleStyle(Style.Parse(Theme.Muted)));

    public static void PrintKeyValue(string key, string value) =>
        AnsiConsole.MarkupLine($"  [bold {Theme.Primary}]{Markup.Escape(key),-14}[/] [white]{Markup.Escape(value)}[/]");

    public static void PrintVersionInfo()
    {
        var brand = GradientMarkup("dbsh", Theme.Primary, Theme.Accent);
        AnsiConsole.MarkupLine($"{brand} [white]v{Markup.Escape(FileVersionInfo.GetVersionString())}[/]");
        AnsiConsole.MarkupLine($"[grey]database migrations for .NET[/]");
    }

    public static async Task RunWithSpinner(string message, Func<Task> action)
    {
        if (UiSuppressed)
        {
            await action();
            return;
        }
        await AnsiConsole.Status()
            .AutoRefresh(true)
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Theme.Primary))
            .StartAsync($"[grey]{Markup.Escape(message)}...[/]", async _ => await action());
    }

    public static async Task<T> RunWithSpinner<T>(string message, Func<Task<T>> action)
    {
        if (UiSuppressed)
        {
            return await action();
        }
        return await AnsiConsole.Status()
            .AutoRefresh(true)
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Theme.Primary))
            .StartAsync($"[grey]{Markup.Escape(message)}...[/]", async _ => await action());
    }

    /// <summary>
    /// Runs a deploy/rollback while streaming one line per migration
    /// (e.g. "✓ 001 CreateUsersTable (0.12s)") above a live spinner.
    /// </summary>
    public static async Task<T> RunWithProgress<T>(string verb, MigrationProgress progress, Func<Task<T>> action)
    {
        if (UiSuppressed)
        {
            return await action();
        }

        return await AnsiConsole.Status()
            .AutoRefresh(true)
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Theme.Primary))
            .StartAsync($"[grey]{Markup.Escape(verb)}...[/]", async ctx =>
            {
                progress.Handler = evt =>
                {
                    switch (evt.Kind)
                    {
                        case MigrationProgressKind.Started:
                            ctx.Status($"[grey]{Markup.Escape(verb)} [bold {Theme.Primary}]{Markup.Escape(evt.Version)}[/] [white]{Markup.Escape(evt.Name)}[/]...[/]");
                            break;
                        case MigrationProgressKind.Completed:
                            AnsiConsole.MarkupLine(ProgressLine(evt, Theme.Check, null));
                            break;
                        case MigrationProgressKind.Failed:
                            AnsiConsole.MarkupLine(ProgressLine(evt, Theme.Cross, $"[bold {Theme.Danger}]failed[/]"));
                            break;
                    }
                };

                return await action();
            });
    }

    private static string ProgressLine(MigrationProgressEvent evt, string glyph, string? suffix)
    {
        var elapsed = evt.Elapsed.TotalSeconds > 0 ? $" [grey]({evt.Elapsed.TotalSeconds:0.00}s)[/]" : string.Empty;
        var tail = string.IsNullOrEmpty(suffix) ? string.Empty : $" {suffix}";
        return $"  {glyph} [bold {Theme.Primary}]{Markup.Escape(evt.Version)}[/] [white]{Markup.Escape(evt.Name)}[/]{elapsed}{tail}";
    }

    public static void PrintMigrationTable(string title, IEnumerable<(string Version, string Name, string Type, string Status, string Detail)> rows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(Style.Parse(Theme.Muted))
            .Title($"[bold {Theme.Primary}]{Markup.Escape(title)}[/]")
            .Expand();

        table.AddColumn(new TableColumn($"[{Theme.Warning}]Version[/]").RightAligned());
        table.AddColumn(new TableColumn($"[{Theme.Warning}]Name[/]").LeftAligned());
        table.AddColumn(new TableColumn($"[{Theme.Warning}]Type[/]").Centered());
        table.AddColumn(new TableColumn($"[{Theme.Warning}]Status[/]").Centered());
        table.AddColumn(new TableColumn($"[{Theme.Warning}]Detail[/]").LeftAligned());

        foreach (var row in rows)
        {
            table.AddRow(
                new Markup($"[bold {Theme.Primary}]{Markup.Escape(row.Version)}[/]"),
                new Markup($"[white]{Markup.Escape(row.Name)}[/]"),
                new Markup($"[grey]{Markup.Escape(row.Type)}[/]"),
                new Markup(StatusMarkup(row.Status)),
                new Markup($"[grey]{Markup.Escape(row.Detail)}[/]"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void PrintSummary(string title, IEnumerable<(string Key, string Value, string Color)> rows)
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(2))
            .AddColumn();

        foreach (var row in rows)
        {
            grid.AddRow(
                new Markup($"[bold {Theme.Primary}]{Markup.Escape(row.Key)}[/]"),
                new Markup($"[{row.Color}]{Markup.Escape(row.Value)}[/]"));
        }

        AnsiConsole.Write(new Panel(grid)
            .Border(BoxBorder.Rounded)
            .BorderStyle(Style.Parse(Theme.Muted))
            .Header($" [{Theme.Primary}] {Markup.Escape(title)} [/]", Justify.Left)
            .Padding(1, 0));
        AnsiConsole.WriteLine();
    }

    public static void PrintBox(string title, string content, string? borderColor = null)
    {
        borderColor ??= Theme.Primary;
        AnsiConsole.Write(new Panel(new Markup($"[white]{Markup.Escape(content)}[/]"))
            .Border(BoxBorder.Rounded)
            .BorderStyle(Style.Parse(borderColor))
            .Header($" [bold {borderColor}] {Markup.Escape(title)} [/]", Justify.Left)
            .Padding(1, 0));
    }

    public static void PrintList(string title, IEnumerable<string> items)
    {
        var rows = items.Select(i => new Markup($"  {Theme.Arrow} [white]{Markup.Escape(i)}[/]")).ToArray();
        AnsiConsole.Write(new Panel(new Rows(rows))
            .Border(BoxBorder.Rounded)
            .BorderStyle(Style.Parse(Theme.Primary))
            .Header($" [bold {Theme.Primary}] {Markup.Escape(title)} [/]", Justify.Left)
            .Padding(1, 0));
    }

    public static bool Confirm(string prompt, bool defaultValue = false) =>
        AnsiConsole.Confirm($"[bold {Theme.Warning}]{Markup.Escape(prompt)}[/]", defaultValue);

    public static void RenderException(Exception ex)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel(new Markup($"[white]{Markup.Escape(ex.Message)}[/]"))
            .Border(BoxBorder.Heavy)
            .BorderStyle(Style.Parse(Theme.Danger))
            .Header($" [{Theme.Danger}] {Markup.Escape(ex.GetType().Name)} [/]", Justify.Left)
            .Padding(1, 0);
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>Renders an expected, user-facing failure - clean message, no internal exception type names.</summary>
    public static void PrintFatal(string message)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel(new Markup($"[white]{Markup.Escape(message)}[/]"))
            .Border(BoxBorder.Heavy)
            .BorderStyle(Style.Parse(Theme.Danger))
            .Header($" [{Theme.Danger}] error [/]", Justify.Left)
            .Padding(1, 0);
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static string StatusMarkup(string status) => status.Trim() switch
    {
        "Completed" or "Applied" or "Success" => $"[bold {Theme.Success}]{status}[/]",
        "Pending" => $"[bold {Theme.Warning}]{status}[/]",
        "Failed" => $"[bold {Theme.Danger}]{status}[/]",
        "RolledBack" or "Rolled Back" => $"[bold {Theme.Accent}]{status}[/]",
        "InProgress" or "In Progress" => $"[bold {Theme.Info}]{status}[/]",
        "Drifted" => $"[bold {Theme.Warning}]{status}[/]",
        "Missing" => $"[bold {Theme.Danger}]{status}[/]",
        _ => $"[grey]{Markup.Escape(status)}[/]"
    };

    /// <summary>Renders an execution outcome as "001 Name (0.12s)" - version-first, no file names.</summary>
    public static string FormatOutcome(MigrationOutcome outcome)
    {
        var elapsed = outcome.ElapsedMs > 0 ? $" ({outcome.ElapsedMs / 1000.0:0.00}s)" : string.Empty;
        return $"{outcome.Version} {outcome.Name}{elapsed}";
    }

    public static string GradientMarkup(string text, string fromHex, string toHex)
    {
        var from = ParseHex(fromHex);
        var to = ParseHex(toHex);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var t = text.Length == 1 ? 0 : (double)i / (text.Length - 1);
            var r = (int)Math.Round(from.R + (to.R - from.R) * t);
            var g = (int)Math.Round(from.G + (to.G - from.G) * t);
            var b = (int)Math.Round(from.B + (to.B - from.B) * t);
            sb.Append($"[bold #{r:X2}{g:X2}{b:X2}]{Markup.Escape(text[i].ToString())}[/]");
        }
        return sb.ToString();
    }

    private static (int R, int G, int B) ParseHex(string hex)
    {
        var h = hex.TrimStart('#');
        return (Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..6], 16));
    }
}

internal static class FileVersionInfo
{
    public static string GetVersionString()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
