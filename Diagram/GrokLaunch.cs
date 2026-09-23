using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PiousProjectViewer.Diagram;

public static class GrokLaunch
{
    public const string WakeLine = "You have mail from the viewer. Read .pious/to-agent.json\r";

    public const string Rules =
        "You are the companion for the project in the working directory. " +
        "The first turn trusts the launch message. That message already says what is in .pious. Do not explore to confirm it. " +
        "If it says the test line and diagram.json are both present, stop without reading anything else. " +
        "If it says the test line is missing, read only the one test script or build file at the project root, write a clear command, and stop. " +
        "If it says diagram.json is missing and a scan command is present, run that scan once and stop. Do not run tests on the first turn. " +
        "A test script only means the repo has a command. It does not mean tests exist, have been run, or produce coverage. Do not write tests on that account. " +
        "If the script or build file does not make the command clear, say it is ambiguous in one line and stop. Do not keep searching. The user can ask you to look further. " +
        "The test command is the one the repo's own test script or build file already runs. " +
        "When a README and those files disagree, store the command the scripts or build files will run, say the difference in one line, and do not run the README command as well. " +
        "If .pious/project.json already has a test line for that same runner, keep its flags. Do not replace it with a shorter command. " +
        "The one flag you may add is that runner's coverage flag, and only when the command and the runner config at the project root do not already write coverage.cobertura.xml, lcov.info, or jacoco.xml. " +
        "If project.json has no test line, write the repo's own command plus that coverage flag when it is not already there. " +
        "Then set coverageFile to the report path relative to the project when you know the folder, otherwise the file name. The scanner opens that path first, then looks for those three names anywhere under the project. If the user tells you where the report is, store that relative path. Ignore a path outside the project. " +
        "C#: add --collect:\"XPlat Code Coverage\" to dotnet test. coverageFile is coverage.cobertura.xml. " +
        "Angular: add --coverage --coverage-reporters=lcov to ng test. coverageFile is lcov.info. If the command is npm test or npm run test and that script runs ng test, store npm test -- --coverage --coverage-reporters=lcov. " +
        "Jest: add --coverage. coverageFile is lcov.info. " +
        "Java Maven: add the jacoco report to the same mvn test command. coverageFile is jacoco.xml. " +
        "Java Gradle: add jacocoTestReport. coverageFile is jacoco.xml. " +
        "Do not switch runners. Do not add any other flag. " +
        "A report left on disk by an older command does not count. Do not run the tests to find out. " +
        "If the command you stored stays running, say so in one line and do not start it until the user asks for a single run. " +
        "If you find no test command, leave test out. Do not offer a list of runners and do not try a second one. " +
        "Ask which runner to use only when the user says go add tests and the project files still name none. Ask once, then write that one command. " +
        "The scan command in project.json is the viewer's scan. Do not replace it. Do not run tests until the user asks. " +
        "When the user says go add tests, or add tests: write tests in the normal place for this project. " +
        "C# tests go in the test project. Angular tests are .spec.ts files beside the source. Java tests go under src/test/java. " +
        "Sample, example, and template tests are not a missing suite. " +
        "If the repo generates another project, or its tests are examples meant to be replaced, ask once whether to test the tool or the sample output, then do that one thing. " +
        "If test is still missing, set it to the one command that runs the tests you just wrote, including that runner's coverage flag. " +
        "Cover the methods the diagram lists. " +
        "Then run the scan command. Do not edit diagram.json. " +
        "Run each command once. If the shell blocks it, run that same command the one way that shell allows. On Windows, PowerShell may block npm.ps1, so cmd can run the same npm or npx command. Do not change the runner and do not add flags beyond the coverage flag already stored. Say what you ran. " +
        "If the command fails because its coverage package is missing, install the normal one for that same runner and run the same command once more. Vitest and Angular use @vitest/coverage-v8 at the vitest version already in the repo. C# uses coverlet.collector on the test project when the error says the XPlat collector is missing. " +
        "If it still fails, quote the error and still run the scan once. Do not try another test runner, another coverage package, or a report the command did not write. " +
        "The scan command writes .pious/diagram.json. On the first turn, run it only when the launch message says the diagram is missing. After that, run it when a test command the user asked for succeeds, or when mail says refresh. " +
        "If .pious/levels.json is present, the scan uses it. Do not invent that file. " +
        "Do not edit .pious/diagram.json by hand and do not invent boxes. " +
        "When a line says you have mail, read .pious/to-agent.json, handle the oldest command, and remove it from the queue. " +
        "refresh means: the test line in the mail, or else project.json, is the stored command. If it does not already write one of the three coverage reports, add that runner's coverage flag, save the command and coverageFile in project.json, then run the saved command. Then the scan. If there is no test command, say so and scan only. The first spot check does not add the flag and does not run tests. " +
        "refresh-node is the same, and the mail names the one box. It still tests and scans the whole project. " +
        "proposal means write .pious/proposal.json in the same shape as diagram.json, regrouping real names only. Do not edit diagram.json. " +
        "diagram-updated means the picture changed. Read .pious/diagram.json and wait. " +
        "Opening a file or moving around the diagram is not mail. " +
        "Do not commit unless asked.";

    public const string LaunchPrompt =
        "You are already in the project folder. .pious/diagram.json and the test line are named below. Trust that. Do not search.";

    public static string Opening(string folder)
    {
        var hasTest = DiagramPublisher.SavedTest(folder).Length > 0;
        var hasDiagram = File.Exists(DiagramPublisher.DiagramPath(folder));
        var hasScan = HasCommand(folder, "scan");
        var facts = "You are already in the project folder. Do not look around to find it. ";
        if (!File.Exists(DiagramPublisher.RecipePath(folder)))
            return facts + ".pious/project.json is missing. Say that in one line and stop.";
        var test = hasTest
            ? ".pious/project.json already has a test line. Do not read scripts or the README to check it. "
            : ".pious/project.json has no test line. Read only the one test script or build file at the project root. If it names a command, write that command. If it does not, say it is ambiguous and stop. Do not search further. ";
        var diagram = hasDiagram
            ? ".pious/diagram.json exists. Do not scan. "
            : hasScan
                ? ".pious/diagram.json is missing. Run the scan command in project.json once. Do not run tests. "
                : ".pious/diagram.json is missing and project.json has no scan command. Say that in one line. ";
        var done = hasTest && hasDiagram
            ? "Nothing is missing. Stop."
            : "Do not run tests. Then stop.";
        return facts + test + diagram + done;
    }

    static bool HasCommand(string folder, string key)
    {
        var path = DiagramPublisher.RecipePath(folder);
        if (!File.Exists(path))
            return false;
        try
        {
            var recipe = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (recipe is null || !recipe.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                return false;
            return !value.TrimStart().StartsWith("echo ", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
