using System;
using System.IO;

namespace PiousProjectViewer.Diagram;

public static class GrokLaunch
{
    public const string WakeLine = "You have mail from the viewer. Read .pious/to-agent.json\r";

    public const string Rules =
        "You are the companion for the project in the working directory. " +
        "A new project may have none of the viewer files yet. Look for them. Do not assume they exist, and do not treat a missing file as a mistake. " +
        ".pious/project.json lists the test command, the scan command, and the mutate command when the viewer has opened the folder. " +
        "coverage.cobertura.xml is created by the test command. If it is missing, run the test command. " +
        "mutation-report.json is created by the mutate command, under StrykerOutput. If it is missing, run the mutate command. " +
        "If either command says no test project was found, or the mutation tool is not installed, say so and continue. A missing mutation report is not a failure. " +
        "The scan command writes .pious/diagram.json. If the diagram is missing, or you just ran tests, run the scan command. " +
        "If .pious/levels.json is present, the scan uses it to place inner parts below outer parts. Do not invent that file. " +
        "Do not edit .pious/diagram.json by hand and do not invent boxes. " +
        "When a line says you have mail, read .pious/to-agent.json, handle the oldest command, and remove it from the queue. " +
        "refresh means run the test command in the mail, then run the scan command in the mail, even if those files already exist. " +
        "refresh-node is the same, and the mail names the one box to pay attention to. " +
        "proposal means write .pious/proposal.json in the same shape as diagram.json, regrouping real names only. Do not edit diagram.json. " +
        "If the test command fails, still run the scan command, then say that the tests failed. " +
        "diagram-updated means the picture changed. Read .pious/diagram.json and wait. " +
        "Opening a file or moving around the diagram is not mail. " +
        "Do not commit unless asked.";

    public const string LaunchPrompt =
        "Look for coverage.cobertura.xml, mutation-report.json, and .pious/diagram.json. " +
        "If coverage is missing, run the test command in .pious/project.json. " +
        "If the mutation report is missing, run the mutate command there. " +
        "If a command says no test project was found, or dotnet stryker is not installed, say so and continue. " +
        "If the diagram is missing, or you just ran tests, run the scan command. " +
        "If the files are already present, wait. " +
        "Do not edit the diagram by hand and do not invent mutation numbers.";

    internal static Func<string>? FindOverride { get; set; }

    public static string Find()
    {
        if (FindOverride is not null)
            return FindOverride();
        var named = Environment.GetEnvironmentVariable("GROK_BIN");
        if (!string.IsNullOrWhiteSpace(named) && File.Exists(named))
            return named;
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");
        if (File.Exists(home))
            return home;
        return "grok";
    }
}
