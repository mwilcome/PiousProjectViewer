# Pious Project Viewer

## Core Technologies

- C# on .NET 10 (`net10.0`)
- Avalonia 12.1.2 for the window, with the Fluent theme
- Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.0.0) for the C# scan
- Iciclecreek.Avalonia.Terminal 4.0.2 for the companion terminal
- The C# scan uses Roslyn. The Angular scan uses the TypeScript parser and labels the template and the stylesheet. The Java scan still reads source as text.

## How it works

You open a folder. The app does not scan it by itself. It writes `.pious/project.json` with the commands for that language, and it shows `.pious/diagram.json` if that file is already there.

Start the companion. It reads `project.json` and runs only the commands listed there, once each. The scan command writes the diagram. The window reloads when that file changes.

A box is a package or a class. An oval is a library. Click a box for its classes or methods in the side panel. Double-click a class for the detail card. Click a method to open that line in VS Code. Right-click a box to ask for a refresh of that box.

## CRAP Formulas/Complexity Calculations

Complexity starts at 1. Each decision adds 1: `if`, `while`, `for`, `foreach`, `do`, `catch`, `switch` arms, `&&`, `||`, `??`, and `? :`.

CRAP for one method, with coverage as a percent from 0 to 100:

`CRAP = CC² × (1 − coverage / 100)³ + CC`

Full coverage makes the score equal the complexity. No coverage makes it `CC² + CC`. A missing coverage file is not a score. Those boxes stay slate.

A class score is the mean (μ), the worst method (max), and the spread (σ). σ divides by the number of methods, not by one less. Each number is rounded to one decimal. The color uses μ + σ: calm at or under 8, warning through 20, hot above 20.

Complexity colors, from the worst method: 1–4 very good, 5–7 good, 8–10 med, 11–20 bad, 21 and above very bad.

A red arrow is a dependency from an inner part to an outer part. Red is not a box color. Inner and outer come from `.pious/levels.json` when that file exists.

## Base Expectations

A folder is ready to scan when it is one of these:

- **C#.** A project file that is not a test project, or a solution.
- **Angular.** `angular.json`, or a `package.json` that depends on `@angular/core`.
- **Java.** `pom.xml`, or a Gradle build file.

You do not need tests, a diagram, a coverage file, or a mutation report before the first open. The viewer writes `.pious/project.json` with the test command and the scan command for that language. The companion writes the diagram by running the scan. The boxes appear without colors.

CRAP colors need a coverage file. You do not type the test command. In the companion, say **go add tests**. It writes tests in the normal place: a C# test project, `*.spec.ts` beside the Angular source, or `src/test/java` for Java. It runs the test command from `project.json`, then the scan. **Refresh diagram** runs the tests that are already there and scans again. It does not write new tests.

If the folder is not one of those three, Auto has nothing to scan. The companion cannot invent a language.

## Explanations

Open the repo and run:

```
dotnet run
```

You need the .NET 10 SDK. The window is the program `PiousProjectViewer`.

**Look at this C# repo.** Open the folder, start the companion, then Refresh diagram. The test command is `dotnet test` on the solution with coverage collection. The scan command rewrites `.pious/diagram.json`. Boxes turn green, gold, or red from CRAP when `coverage.cobertura.xml` is present.

**Look at an Angular app.** The folder needs `angular.json`, or a `package.json` that depends on `@angular/core`. Auto picks Angular when most of the source is TypeScript. If `*.spec.ts` files exist, the test command is `npx ng test --watch=false --coverage --coverage-reporters=lcov`, run through `cmd` so PowerShell does not block `npx`. CRAP reads `lcov.info`. That report needs `@vitest/coverage-v8` on current Angular. If there are no spec files, there is no test command.

**Look at a Java project.** The folder needs `pom.xml` or a Gradle build. If tests exist, Maven runs tests with JaCoCo and CRAP reads `jacoco.xml`. Gradle runs `test jacocoTestReport`, which only writes that file when the build already applies JaCoCo. If there are no tests, there is no test command.

**What you should expect.** `.pious/` is generated. It is gitignored here. A new project does not need a diagram, a coverage file, or a mutation report waiting on disk. Missing coverage leaves the boxes slate. It is not a failure. Mutation numbers show up only when a `mutation-report.json` is already there. C# can run Stryker. Angular and Java have no mutate command.

**What the companion does when you start it.** It reads `.pious/project.json`. If the diagram is missing, it runs the scan and waits. It does not run tests until you ask. Say **go add tests** and it writes them, runs the test command from that file, then the scan. If a command fails, it quotes the error and stops retrying. It does not edit `diagram.json` or invent boxes. Refresh diagram asks it to run the tests and then the scan again. Ask for a proposal asks it to write `.pious/proposal.json`. That button stays off until the companion is running.

Screenshots can be added later.

## Future Plans

- macOS, then other desktops
- Companions other than the Grok CLI
- More languages, on the same scan and test-command split
- A real parser for Java, in place of the text scan

## Credits

Mike  
[mwilcomesc@gmail.com](mailto:mwilcomesc@gmail.com)  
[piousprogrammer.com](https://piousprogrammer.com)

Inspired by Uncle Bob Martin's [uml-viewer](https://github.com/unclebob/uml-viewer). This is a separate program.
