using System;
using System.IO;

namespace PiousProjectViewer.Diagram;

public static class GrokLaunch
{
    public const string WakeLine = "You have mail from the viewer. Read .pious/to-agent.json\r";

    public const string Rules =
        "You are the companion for the project in the working directory. " +
        "A new project may have none of the viewer files yet. Look for them. Do not assume they exist, and do not treat a missing file as a mistake. " +
        ".pious/project.json lists the test command and the scan command when the viewer has opened the folder. " +
        "coverage.cobertura.xml is created by the test command. If it is missing, run the test command. " +
        "If that command says no test project was found, say so and continue. There is no mutation file and you do not create one. " +
        "The scan command writes .pious/diagram.json. If the diagram is missing, or you just ran tests, run the scan command. " +
        "If .pious/levels.json is present, the scan uses it to place inner parts below outer parts. Do not invent that file. " +
        "Do not edit .pious/diagram.json by hand and do not invent boxes. " +
        "When a line says you have mail, read .pious/to-agent.json, handle the oldest command, and remove it from the queue. " +
        "refresh means run the test command in the mail, then run the scan command in the mail, even if those files already exist. " +
        "If the test command fails, still run the scan command, then say that the tests failed. " +
        "diagram-updated means the picture changed. Read .pious/diagram.json and wait. " +
        "Opening a file or moving around the diagram is not mail. " +
        "Do not commit unless asked.";

    public const string LaunchPrompt =
        "Look for coverage.cobertura.xml and .pious/diagram.json. " +
        "If coverage is missing, run the test command in .pious/project.json. " +
        "If the diagram is missing, or you just ran tests, run the scan command there. " +
        "If both are already present, wait. " +
        "Do not look for mutation files. Do not edit the diagram by hand.";

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
