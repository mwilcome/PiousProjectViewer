using System;
using System.IO;

namespace PiousProjectViewer.Diagram;

public static class GrokLaunch
{
    public const string WakeLine = "You have mail from the viewer. Read .pious/to-agent.json\r";

    public const string Rules =
        "You are the companion for the project in the working directory. " +
        "Read .pious/project.json. The test, scan, and mutate commands are there when this language has them. Do not invent a different command. " +
        "On start, if .pious/diagram.json is missing, run the scan command and wait. Do not run tests until the user asks. " +
        "When the user says go add tests, or add tests: write tests in the normal place for the scanner in project.json. " +
        "C# tests go in the test project. Angular tests are .spec.ts files beside the source. Java tests go under src/test/java. " +
        "Cover the methods the diagram lists. If the coverage tool for that test command is not installed, install the normal one and run the test command once more. " +
        "Then run the scan command. Do not edit diagram.json. " +
        "Run each command once. If it fails, quote the error and go on. Do not try another shell. Do not search again for a report the command did not write. " +
        "The scan command writes .pious/diagram.json. Run it when the diagram is missing, or after a test command succeeds. " +
        "If .pious/levels.json is present, the scan uses it. Do not invent that file. " +
        "Do not edit .pious/diagram.json by hand and do not invent boxes. " +
        "When a line says you have mail, read .pious/to-agent.json, handle the oldest command, and remove it from the queue. " +
        "refresh means run the test command in the mail if it is present, then the scan command. " +
        "refresh-node is the same, and the mail names the one box. " +
        "proposal means write .pious/proposal.json in the same shape as diagram.json, regrouping real names only. Do not edit diagram.json. " +
        "diagram-updated means the picture changed. Read .pious/diagram.json and wait. " +
        "Opening a file or moving around the diagram is not mail. " +
        "Do not commit unless asked.";

    public const string LaunchPrompt =
        "Read .pious/project.json. If .pious/diagram.json is missing, run the scan command and wait. " +
        "Do not run tests until the user asks. " +
        "If the user says go add tests, write the tests for this language, run the test command from project.json once, then the scan command. " +
        "Do not edit the diagram by hand and do not invent commands or numbers.";

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
