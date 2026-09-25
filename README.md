# Pious Project Viewer

## Core Technologies

| Technology | Version | Role |
| --- | --- | --- |
| .NET | 10 (`net10.0`) | Runtime the window is built on |
| Avalonia | 12.1.2 | Window and controls |
| Avalonia.Desktop | 12.1.2 | Desktop host |
| Avalonia.Themes.Fluent | 12.1.2 | Fluent theme |
| Avalonia.Fonts.Inter | 12.1.2 | Inter font |
| Microsoft.CodeAnalysis.CSharp | 5.0.0 | Roslyn scan of C# |
| Iciclecreek.Avalonia.Terminal | 4.0.2 | Companion terminal |
| TypeScript | 5.9.3 | Angular class scan in `scanners/angular` |
| Styles scan | none | In-app read of HTML and stylesheets on the Styles tab |
| Java scan | none | Reads `.java` as text. No parser package. |

## How it works

You open a folder. The app does not scan it, write `.pious`, or start the companion. If `.pious/diagram.json` is already there, that picture opens. Language starts on Auto. Detection looks only at the opened folder.

A recognized folder with no `.pious` yet offers **Generate project and start agent**. That writes `.pious/project.json` with the scan command and starts the companion. The app does not fill in the test command. The companion reads the one test script or build file at the project root and stores the command it finds.

On that first turn the companion trusts what `.pious` already contains. If the test line and the diagram are both there, it stops. If the diagram is missing and a scan command is present, it runs that scan once. It does not run tests until you ask. The window reloads when `diagram.json` changes. Opening a different folder stops a companion that is still running.

A box is a package or a class. An oval is a library. The left side lists the classes or methods. Double-click a class for the detail card. Click a method, or double-click a method line, to open the file in the default app. Right-click a box to refresh. That runs the tests and scans the whole project. The mail only names the box.

Color by is Complexity, CRAP, or Distance. **Generate proposal** asks the companion to move one type into another folder and write `.pious/proposal.json`. **Switch to proposal** shows that file locally. It does not scan again.

An Angular, React, Vue, or Svelte folder also has a Styles tab. The picture is the templates and the stylesheets that hit them. JSX uses `className`. Vue and Svelte styles are read from the component file. **Refresh styles** rescans in the app. It does not run tests and does not need the companion. If `.pious` already exists, it writes `.pious/styles-diagram.json`. Click an HTML box to see which styles hit it and which file each one comes from. Click a style to open that file. **Generate proposal** on this tab asks for one style-rule change in `.pious/styles-proposal.json`.

## CRAP Formulas/Complexity Calculations

Complexity starts at 1. Each decision adds 1: `if`, `while`, `for`, `foreach`, `do`, `catch`, `switch` arms, `&&`, `||`, `??`, and `? :`.

CRAP for one method, with coverage as a percent from 0 to 100:

`CRAP = CC² × (1 − coverage / 100)³ + CC`

Full coverage makes the score equal the complexity. No coverage makes it `CC² + CC`. A missing coverage report is treated as 0% covered, so CRAP still colors. A method that an existing report does not mention stays unscored, and that box stays slate.

A class score is the mean (μ), the worst method (max), and the spread (σ). σ divides by the number of methods, not by one less. Each number is rounded to one decimal. The color uses μ + σ: calm at or under 8, warning through 20, hot above 20.

Complexity colors, from the worst method: 1–4 very good, 5–7 good, 8–10 med, 11–20 bad, 21 and above very bad.

A red arrow is a dependency from an inner part to an outer part. Red is not a box color. Inner and outer come from `.pious/levels.json` when that file exists.

Distance does not use tests. For a package, instability is outgoing types divided by incoming plus outgoing types. Abstractness is abstract types divided by types. Distance is the absolute value of abstractness plus instability minus 1. Test packages are left out. C# counts interfaces and abstract classes as abstract. Angular and Java do not mark abstract types yet, so their distance is instability alone. In this color mode the package words come from that distance. A class box is colored by how many other types it links to.

## Base Expectations

A folder is ready to scan when the opened folder itself is one of these. The app does not search child folders.

- **C#.** A solution or a project file.
- **Angular.** `angular.json`, or a `package.json` that depends on `@angular/core`.
- **Java.** `pom.xml`, `build.gradle`, or `build.gradle.kts`.

You do not need tests, a diagram, a coverage file, or a mutation report before the first open. **Generate project and start agent** writes `.pious/project.json` with the scan command. The test line stays empty until the companion stores the command from the project. The companion writes the diagram by running the scan. CRAP colors appear even when no coverage report was measured, using 0% for every method. A real report replaces that. A method the report does not mention is not given a score.

You do not type the test command. In the companion, say **go add tests**. It writes tests in the normal place: a C# test project, `*.spec.ts` beside the Angular source, or `src/test/java` for Java. It runs the test command from `project.json`, then the scan. **Refresh diagram** runs the tests that are already there and scans again. It does not write new tests.

If the folder is not one of those three, Auto has nothing to scan. The companion cannot invent a language.

## Explanations

Open the repo and run:

```
dotnet run
```

You need the .NET 10 SDK. The window is the program `PiousProjectViewer`.

**Look at this C# repo.** Open the folder, then **Generate project and start agent** if `.pious` is not there yet. The companion stores the test command the repo already uses. On refresh it adds `--collect:"XPlat Code Coverage"` only when that command does not already write `coverage.cobertura.xml`. The scan command rewrites `.pious/diagram.json`. A `.razor` file is a component on its code-behind, or its own box when there is no code-behind.

**Look at an Angular app.** The opened folder needs `angular.json`, or a `package.json` that depends on `@angular/core`. The test line is the command that project's own test script runs. On refresh, `ng test` can gain `--coverage --coverage-reporters=lcov`. If the command is `npm test` and that script runs `ng test`, the stored line is `npm test -- --coverage --coverage-reporters=lcov`, when it does not already write `lcov.info`. That report needs `@vitest/coverage-v8` on current Angular. If PowerShell blocks `npm.ps1`, the companion runs the same command through `cmd`. React, Vue, and Svelte use the same picture. The opened folder's `package.json` picks the flavor. The Styles tab reads HTML, JSX `className`, and Vue or Svelte style blocks. It does not use the test command.

**Look at a Java project.** The opened folder needs `pom.xml`, `build.gradle`, or `build.gradle.kts`. The test line is the command that build file already runs. On refresh, Maven keeps the same `mvn test` and adds the JaCoCo report. Gradle adds `jacocoTestReport`. CRAP reads `jacoco.xml`. That Gradle report is written only when the build already applies JaCoCo. If the build file names no test command, the test line stays empty. Interfaces and abstract classes count as abstract. Each file is read by its braces.

**What you should expect.** `.pious/` is generated. It is gitignored here. A new project does not need a diagram, a coverage file, or a mutation report waiting on disk. A missing coverage report is treated as 0% covered. It is not a failure. Mutation numbers show up only when a `mutation-report.json` is already there. C# can run Stryker. Angular and Java have no mutate command.

**What the companion does when you start it.** It reads `.pious/project.json`. If the test line and the diagram are both present, it stops. If the test line is missing, it reads only the one root test script or build file. If the diagram is missing, it runs the scan once. It does not run tests, and it does not add a coverage flag, until you ask. Say **go add tests** and it writes them, runs the test command from that file, then the scan. The one flag it may add is that runner's coverage flag, and only when the command and the root runner config do not already write `coverage.cobertura.xml`, `lcov.info`, or `jacoco.xml`. If the command fails because that coverage package is missing, it installs the normal package for the same runner and runs the command once more. If it still fails, it quotes the error and still scans once. It does not edit `diagram.json` or invent boxes. **Refresh diagram** asks it to run the tests and then the scan again. **Generate proposal** asks it to write `.pious/proposal.json`, or `.pious/styles-proposal.json` when the Styles tab is showing. That button stays off until the companion is running. **Refresh styles** does not ask the companion.

## Future Plans

- macOS, then other desktops
- Companions other than the Grok CLI
- More languages, on the same scan and test-command split
- A real parser for Java, in place of the text scan

## Credits

Inspired by Uncle Bob Martin's [uml-viewer](https://github.com/unclebob/uml-viewer). This is a separate program loosely based on the UML viewer and CRAP analysis. 
