using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public class ListSendToCommand : Command<ListSendToCommand.Settings>
{
    public class Settings : CommandSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var services = new ServiceCollection();
        Program.ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var weChat = provider.GetRequiredService<IWeChatService>();

        return Execute(weChat);
    }

    private static int Execute(IWeChatService weChat)
    {
        var candidates = weChat.GetSendToCandidates();

        if (candidates.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No send-to candidates configured.[/]");
            AnsiConsole.MarkupLine("\nConfigure targets in [cyan]appsettings.json[/] under [bold]WeChat:UserId[/] and [bold]WeChat:ToUsers[/].");
            return 0;
        }

        AnsiConsole.Write(new FigletText("Send To")
            .LeftJustified()
            .Color(Color.Cyan1));

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold]ID[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Label[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Kind[/]").Centered());

        foreach (var c in candidates)
        {
            table.AddRow(
                c.Id,
                c.Label,
                c.Kind
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[grey]Total: {candidates.Count} candidate(s)[/]");

        return 0;
    }
}
