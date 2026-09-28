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

## Installation Instructions

The viewer needs the .NET 10 SDK. The companion needs the Grok CLI. Angular, React, Vue, and Svelte class scans need Node.js, plus the TypeScript package in `scanners/angular`. C# and Java class scans do not need Node. A Java folder you open still needs a JDK on `PATH`, and a Maven folder needs `mvn` unless that repo has `mvnw`.

Start the viewer from a terminal where those commands already work. The window inherits that `PATH`, and so does the companion and the TypeScript scan. Open a new terminal after an installer changes `PATH`.

`GROK_BIN` is optional. Set it to the full path of the `grok` binary only when that file is not in the default folder below. The app checks `GROK_BIN` first, then the default folder, then `grok` on `PATH`.

### macOS

Install the .NET 10 SDK for your Mac from [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0). Use Arm64 when `uname -m` prints `arm64`. Use x64 when it prints `x86_64`. The package installer puts `dotnet` on `PATH`.

If you use the install script instead, it lands in `~/.dotnet` and does not update your shell:

```
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0
```

Install the Grok CLI:

```
curl -fsSL https://x.ai/cli/install.sh | bash
```

That writes `~/.grok/bin/grok`. Install Node.js from [https://nodejs.org](https://nodejs.org), or with `brew install node`, so `node` and `npm` are on `PATH`. If you use nvm, the terminal you start the viewer from must already see `node`.

Add this to `~/.zshrc` when `dotnet` or `grok` is not found in a new terminal. The script install of .NET needs the `DOTNET_ROOT` lines. The package install of .NET does not.

```
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$HOME/.grok/bin:$PATH"
```

Open a new terminal, then check:

```
dotnet --version
grok --version
node --version
npm --version
```

`dotnet --version` starts with `10.`. From the viewer repo, install the scan package and start the window:

```
npm install --prefix scanners/angular
dotnet run
```

There is no double-clickable app. Clicking a file uses `open`. The terminal font falls back to Menlo.

For a Java folder, `java -version` must work. Maven needs `mvn -version` unless the repo has `mvnw`. Gradle uses `./gradlew` in that repo. If the shell says permission denied, run `chmod +x gradlew` once in that repo. Remember project writes `~/Library/Application Support/pious-project-viewer/session.json`.

Set a custom companion binary in `~/.zshrc` only when it is not `~/.grok/bin/grok`:

```
export GROK_BIN="$HOME/.grok/bin/grok"
```

### Windows

In PowerShell:

```
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS
irm https://x.ai/cli/install.ps1 | iex
```

The .NET installer puts `dotnet` on `PATH`, usually from `C:\Program Files\dotnet`. The Node installer puts `node` and `npm` on `PATH`. The Grok installer writes `%USERPROFILE%\.grok\bin\grok.exe` and adds that folder to your user `PATH`.

Open a new PowerShell window, then check:

```
dotnet --version
grok --version
node --version
npm --version
where.exe dotnet
where.exe grok
where.exe node
```

`dotnet --version` starts with `10.`. If `where.exe` misses one of them, add that program's folder to your user `PATH` and open another window.

From the viewer repo:

```
npm install --prefix scanners/angular
dotnet run
```

If PowerShell blocks `npm.ps1`, run the same `npm install --prefix scanners/angular` in `cmd`. The companion does the same for a later `npm` or `npx` test command: one run through `cmd`, same command.

For a Java folder, `java -version` must work. Maven needs `mvn -version` unless the repo has `mvnw`. Gradle uses `gradlew.bat` in that repo. Remember project writes `%AppData%\pious-project-viewer\session.json`.

Set a custom companion binary only when it is not `%USERPROFILE%\.grok\bin\grok.exe`. In PowerShell, then open a new window:

```
setx GROK_BIN "%USERPROFILE%\.grok\bin\grok.exe"
```

## How it works

You open a folder. The app does not scan it, write `.pious`, or start the companion. If `.pious/diagram.json` is already there, that picture opens. Language starts on Auto. Detection looks only at the opened folder.

A recognized folder with no `.pious` yet offers **Generate project and start agent**. That writes `.pious/project.json` with the scan command and starts the companion. The scan command is this copy of the viewer, on the computer where you generated the project. Generate again if you move the folder to another computer. The app does not fill in the test command. The companion reads the one test script or build file at the project root and stores the command it finds.

On that first turn the companion trusts what `.pious` already contains. If the test line and the diagram are both there, it stops. If the diagram is missing and a scan command is present, it runs that scan once. It does not run tests until you ask. The window reloads when `diagram.json` changes. Opening a different folder stops a companion that is still running.

A box is a package or a class. An oval is a library. The left side lists the classes or methods. Double-click a class for the detail card. Click a method, or double-click a method line, to open the file in the default app. Right-click a box to refresh. That runs the tests and scans the whole project. The mail only names the box.

Color by is Complexity, CRAP, or Distance. **Propose fix** on the class picture asks the companion to move one type into another folder and write `.pious/proposal.json`. The diagram file must already exist. **Switch to proposal** shows that file locally. It does not scan again.

An Angular, React, Vue, or Svelte folder also has a Styles tab. The picture is the seven style homes and the two jobs: fold a class into a shared file, or give a copied name one home. **Refresh styles** rescans in the app. It does not run tests and does not need the companion. If `.pious` already exists, opening Styles or refreshing writes `.pious/styles-diagram.json`. Click a stylesheet or an HTML file to open it. **Propose fix** writes that diagram again, then asks for one style fix in `.pious/styles-proposal.json`. The scan stays until you apply the fix later.

## Use cases

Each case says what you do, what must already be on disk, and what the companion is allowed to do.

**Open a folder.** The app does not write `.pious`, does not scan, and does not start the companion. A diagram already in `.pious/diagram.json` is shown. Anything else stays on the empty screen.

**Generate project and start agent.** The folder is recognized and `.pious` is missing. The app writes `.pious/project.json` with the scan command and starts the companion. The scan command is this copy of the viewer. A folder moved to another computer needs that line written again. Before the companion starts, the window asks about tests. **Scan only** stores `tests` as `none`. The companion does not read a test script, does not write a test line, and does not run tests. It scans when `diagram.json` is missing. **Look once** leaves the test line empty, and the companion reads one root test script or build file. The same question appears on **Start companion** when a project is already generated and still has no test command. The checkbox **No tests, scan only** stores that choice. A scan skips `node_modules` and any folder the system will not let it read. One blocked folder does not throw the diagram away.

**Refresh diagram.** The companion is running and `.pious/project.json` has a scan command. The mail names the test command, when one is stored, and the scan command. The companion runs the test command, then the scan. The scan writes `.pious/diagram.json`. The window reloads that file. If there is no test command, it says so and scans only.

**Refresh this box.** Same as refresh diagram. The mail names the box. The tests and the scan still cover the whole project.

**Propose a class move.** `.pious/diagram.json` is already on disk. If it is not, the app does not send mail and says to refresh the diagram first. The companion reads that file and writes `.pious/proposal.json`. It changes which folder one type sits in. It does not edit `diagram.json` or the source. The picture stays on the scan until you switch.

**Switch to the class proposal.** `.pious/proposal.json` exists. The switch is local. It does not start the companion and does not scan.

**Open Styles.** The folder is a frontend the app recognizes. The scan runs in the app. If `.pious` already exists, the app writes `.pious/styles-diagram.json` before you ask for a fix. The companion is not required.

**Propose a style fix.** The companion is running, a class is selected, and `.pious` exists. The app writes `.pious/styles-diagram.json` from the scan on screen, then sends mail that names `.pious/styles-proposal.json` and that class. The companion reads the diagram file and writes only the proposal file. If the diagram file is missing, it says the path and stops. It does not edit stylesheets or `styles-diagram.json`. The picture stays on the scan until you switch.

**Switch to the style proposal.** `.pious/styles-proposal.json` exists and can be read. The switch is local. **How it is** returns to the scan.

**Go add tests.** You say this to the companion. It is not a button. It writes tests in the normal place, stores the test command if it was missing, runs that command once, then runs the scan. Sample tests are not treated as a missing suite.

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

Install for your system is in Installation Instructions. From the viewer repo, in a terminal where `dotnet` already works:

```
dotnet run
```

The window is the program `PiousProjectViewer`. Clicking a file opens it in the default app. On macOS that is `open`. On Linux it is `xdg-open`. The companion terminal uses Cascadia Mono on Windows and Menlo on macOS.

**Look at this C# repo.** Open the folder, then **Generate project and start agent** if `.pious` is not there yet. The companion stores the test command the repo already uses. On refresh it adds `--collect:"XPlat Code Coverage"` only when that command does not already write `coverage.cobertura.xml`. The scan command rewrites `.pious/diagram.json`. A `.razor` file is a component on its code-behind, or its own box when there is no code-behind.

**Look at an Angular app.** The opened folder needs `angular.json`, or a `package.json` that depends on `@angular/core`. The test line is the command that project's own test script runs. On refresh, `ng test` can gain `--coverage --coverage-reporters=lcov`. If the command is `npm test` and that script runs `ng test`, the stored line is `npm test -- --coverage --coverage-reporters=lcov`, when it does not already write `lcov.info`. That report needs `@vitest/coverage-v8` on current Angular. On Windows, if PowerShell blocks `npm.ps1`, the companion runs the same command through `cmd`. On macOS it runs that command in the normal shell and does not prefix it with `cmd`. React, Vue, and Svelte use the same picture. The opened folder's `package.json` picks the flavor. The Styles tab reads HTML, JSX `className`, and Vue or Svelte style blocks. It does not use the test command.

**Look at a Java project.** The opened folder needs `pom.xml`, `build.gradle`, or `build.gradle.kts`. The test line is the command that build file already runs. On refresh, Maven keeps the same `mvn test` and adds the JaCoCo report. Gradle adds `jacocoTestReport`. CRAP reads `jacoco.xml`. That Gradle report is written only when the build already applies JaCoCo. If the build file names no test command, the test line stays empty. On macOS, if the stored command is `./gradlew` and the shell denies it, the companion runs `bash ./gradlew` once and leaves the stored command as it is. Interfaces and abstract classes count as abstract. Each file is read by its braces.

**What you should expect.** `.pious/` is generated. It is gitignored here. A new project does not need a diagram, a coverage file, or a mutation report waiting on disk. A missing coverage report is treated as 0% covered. It is not a failure. Mutation numbers show up only when a `mutation-report.json` is already there. C# can run Stryker. Angular and Java have no mutate command.

**What the companion does when you start it.** It reads `.pious/project.json`. If the test line and the diagram are both present, it stops. If the test line is missing, it reads only the one root test script or build file. If the diagram is missing, it runs the scan once. It does not run tests, and it does not add a coverage flag, until you ask. Say **go add tests** and it writes them, runs the test command from that file, then the scan. The one flag it may add is that runner's coverage flag, and only when the command and the root runner config do not already write `coverage.cobertura.xml`, `lcov.info`, or `jacoco.xml`. If the command fails because that coverage package is missing, it installs the normal package for the same runner and runs the command once more. If it still fails, it quotes the error and still scans once. It does not edit `diagram.json` or invent boxes. **Refresh diagram** asks it to run the tests and then the scan again. **Propose fix** on the class picture asks it to read `.pious/diagram.json` and write `.pious/proposal.json`. The app does not send that mail when the diagram file is missing. On Styles, the app writes `.pious/styles-diagram.json` first, then asks for `.pious/styles-proposal.json`. If a file the mail names is missing, the companion says the path and stops. The button stays off until a class is selected and the companion is running. **Refresh styles** does not ask the companion.

## Future Plans

- A double-clickable macOS application bundle
- Linux and other desktops
- Companions other than the Grok CLI
- More languages, on the same scan and test-command split
- A real parser for Java, in place of the text scan

## Credits

Inspired by Uncle Bob Martin's [uml-viewer](https://github.com/unclebob/uml-viewer). This is a separate program loosely based on the UML viewer and CRAP analysis. 
