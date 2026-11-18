# Citavi 7 on Apple Silicon with CrossOver or Wine

One command, `./citavi-crossover setup`, turns a Mac that has CrossOver or Wine into a Mac with
a working Citavi 7. It creates the bottle (the folder that holds a Windows installation),
installs Microsoft .NET Framework 4.8 and Citavi, applies the fixes Citavi needs to run under
Wine, and re-applies them after every Citavi update. One more command,
`./citavi-crossover uninstall`, removes it all again.

Tested with Citavi 7.4.1.3, CrossOver 26.3 and Wine Stable 11.0, macOS 27.2 on Apple Silicon.

**In this file:** [What to expect](#what-to-expect) ·
[CrossOver or Wine](#crossover-or-wine) ·
[Quick start with CrossOver](#quick-start-with-crossover) ·
[Quick start with Wine](#quick-start-with-wine) ·
[If something goes differently](#if-something-goes-differently) ·
[Everyday commands](#everyday-commands) · [Updating](#when-a-new-citavi-version-comes-out) ·
[Removing it](#removing-it) · [Notes on Wine](#notes-on-wine) · [Known limits](#known-limits)

## What to expect

- **What works:** local and cloud projects, signing in, the PDF preview, the word processor,
  sharp text on a Retina display, and text in Latin, Greek, Cyrillic and Hebrew letters.
- **What does not:** the Word add-in and the browser Pickers, which are Windows integrations.
  Arabic, Chinese, Japanese and Korean text is invisible in Citavi's text fields. The full list
  is under [Known limits](#known-limits).
- **This is not Citavi for the Mac.** Citavi's maker does not support Citavi on a Mac. The tool
  is a user's workaround: it changes some of Citavi's program files inside the bottle so that
  they run under Wine. Your Citavi projects are not changed. They stay where Citavi keeps them,
  in `Documents/Citavi 7` and in the cloud.

## CrossOver or Wine

Citavi is a Windows program. Both CrossOver and Wine run Windows programs on a Mac, and
CrossOver is built on Wine. The tool works with either.

| | CrossOver | Wine |
|---|---|---|
| Price | paid, with a free trial of 14 days | free |
| To install it | download it and open it once | two Homebrew commands, or a download; then one command so that macOS lets it start |
| Setup takes | about 15 minutes | about 20 minutes |
| Citavi is started from | CrossOver's window | the app *Citavi 7* in `~/Applications`, which setup makes |
| Tested with | CrossOver 26.3 | Wine Stable 11.0 |

There is a Quick start for each: [with CrossOver](#quick-start-with-crossover) and
[with Wine](#quick-start-with-wine). Each is complete by itself, so follow one and skip the
other.

## Quick start with CrossOver

You need an Apple Silicon Mac, [CrossOver](https://www.codeweavers.com/crossover) (the free trial
or a licence), 6 GB of free disk space, an internet connection, and a Citavi account to sign in
with. Nothing else: no Homebrew, no developer tools.

**1. Install CrossOver and open it once.** Answer its questions (trial or licence; if it offers to
install Rosetta, accept). Then quit it.

**2. Get this folder.** Open Terminal (it is in *Applications ▸ Utilities*), paste this line and
press Return. It downloads the folder into your home folder as `citavi-crossover`:

```bash
cd ~ && mkdir citavi-crossover && curl -L https://github.com/AthanasiusOfAlex/citavi-crossover/archive/refs/heads/main.tar.gz | tar xz --strip-components 1 -C citavi-crossover
```

Other ways: on the project's GitHub page, *Code ▸ Download ZIP*, then unpack the ZIP and move the
folder to where it can stay; or `git clone`. Keep the folder: you use it again for updates and
for removing Citavi.

**3. Go into the folder.**

```bash
cd ~/citavi-crossover
```

If the folder is somewhere else: type `cd ` with a space after it, drag the folder from Finder
into the Terminal window, and press Return.

**4. Run setup.**

```bash
./citavi-crossover setup
```

It first checks the Mac and tells you exactly what to do if something is missing. Then it asks
two yes-or-no questions, both because the installers run without showing their licence screens:
whether you accept Microsoft's licence terms for the .NET Framework, and Citavi's. After that it
needs nothing from you for about 15 minutes. Small Windows progress windows come and go. It:

1. creates a CrossOver bottle named *Citavi 7*;
2. downloads Microsoft .NET Framework 4.0 and 4.8 from Microsoft (120 MB) and installs them;
3. applies the registry fixes and copies the fonts;
4. downloads Citavi's installer from Citavi (160 MB) and installs it;
5. patches seven of Citavi's program files and installs the login helper;
6. checks everything and ends with **Citavi is ready.**

If it stops, run the same command again: it continues where it left off.

**5. Start Citavi and sign in.** Setup offers to start Citavi when it has finished. Later, start
it from CrossOver's window, where Citavi has an icon under the *Citavi 7* bottle, or with

```bash
./citavi-crossover run
```

The first time, Citavi shows a short welcome assistant: click **Let's go**, then **Close**. If
you have Citavi 6 projects in `Documents/Citavi 6`, the assistant copies them to
`Documents/Citavi 7`.

Click **Log in** in Citavi. Your browser opens Citavi's sign-in page. After you sign in, the
browser asks whether to open *Citavi Login Helper*: allow it, and Citavi continues on its
"Remember me" page.

If CrossOver was open during setup, it shows the new bottle only after you quit and reopen it.

That is all. The next section is the same route for Wine; go on at
[If something goes differently](#if-something-goes-differently) or
[Everyday commands](#everyday-commands).

## Quick start with Wine

You need an Apple Silicon Mac, 6 GB of free disk space, an internet connection, and a Citavi
account to sign in with. Wine itself is free. [Homebrew](https://brew.sh) is the easiest way to
install it, and step 1 also says how to do without. No developer tools are needed.

**1. Install Wine Stable.** Open Terminal (it is in *Applications ▸ Utilities*). With Homebrew,
paste these two lines one after the other, pressing Return after each. The first adds
`gcenx/wine`, the Homebrew tap (a source of packages) that provides Wine Stable. Homebrew's own
list stopped offering it in September 2026, so without the tap the second line fails.

```bash
brew tap gcenx/wine
```

```bash
brew install --cask wine-stable
```

Without Homebrew: the macOS section of
[Wine's download page](https://gitlab.winehq.org/wine/wine/-/wikis/Download) links to the same
app. Download it, unpack it and move *Wine Stable* to Applications.

**2. Install Rosetta, if the Mac does not have it.** Wine needs Rosetta, Apple's translator for
programs built for Intel processors. If you do not know whether it is there, go on to the next
step: setup checks, and names this command if Rosetta is missing.

```bash
softwareupdate --install-rosetta
```

**3. Let macOS start Wine.** macOS marks every downloaded app and refuses to start one that no
registered developer signed. Wine is not signed, so take the mark off:

```bash
xattr -dr com.apple.quarantine "/Applications/Wine Stable.app"
```

**4. Get this folder.** Paste this line and press Return. It downloads the folder into your home
folder as `citavi-crossover`:

```bash
cd ~ && mkdir citavi-crossover && curl -L https://github.com/AthanasiusOfAlex/citavi-crossover/archive/refs/heads/main.tar.gz | tar xz --strip-components 1 -C citavi-crossover
```

Other ways: on the project's GitHub page, *Code ▸ Download ZIP*, then unpack the ZIP and move the
folder to where it can stay; or `git clone`. Keep the folder: you use it again for updates and
for removing Citavi.

**5. Go into the folder.**

```bash
cd ~/citavi-crossover
```

If the folder is somewhere else: type `cd ` with a space after it, drag the folder from Finder
into the Terminal window, and press Return.

**6. Run setup.**

```bash
./citavi-crossover setup
```

If CrossOver is on this Mac too, the tool would use CrossOver. Say that you mean Wine:

```bash
CITAVI_ENGINE=wine ./citavi-crossover setup
```

In that case put `CITAVI_ENGINE=wine` before every later command as well (`run`, `doctor`,
`update`, `uninstall`). Or make Wine the standing choice: in `config.sh`, change the `ENGINE=`
line to `ENGINE="${CITAVI_ENGINE:-wine}"`.

Setup first checks the Mac and tells you exactly what to do if something is missing. Then it asks
two yes-or-no questions, both because the installers run without showing their licence screens:
whether you accept Microsoft's licence terms for the .NET Framework, and Citavi's. After that it
needs nothing from you for about 20 minutes. Small Windows progress windows come and go. It:

1. creates a Wine bottle named *Citavi 7* in
   `~/Library/Application Support/citavi-crossover/Bottles`;
2. downloads Microsoft .NET Framework 4.0 and 4.8 from Microsoft (120 MB) and installs them;
3. applies the registry fixes and copies the fonts;
4. downloads Citavi's installer from Citavi (160 MB) and installs it;
5. patches seven of Citavi's program files, installs the login helper and makes the app
   *Citavi 7*;
6. checks everything and ends with **Citavi is ready.**

If it stops, run the same command again: it continues where it left off.

**7. Start Citavi and sign in.** Setup offers to start Citavi when it has finished. Later, start
it with the app *Citavi 7*, which setup put into `~/Applications`. Spotlight and Launchpad find
it, and it can be kept in the Dock. This works too:

```bash
./citavi-crossover run
```

While Citavi runs, the Dock shows it under the name *wine* with a plain icon.

The first time, Citavi shows a short welcome assistant: click **Let's go**, then **Close**. If
you have Citavi 6 projects in `Documents/Citavi 6`, the assistant copies them to
`Documents/Citavi 7`.

Click **Log in** in Citavi. Your browser opens Citavi's sign-in page. After you sign in, the
browser asks whether to open *Citavi Login Helper*: allow it, and Citavi continues on its
"Remember me" page.

[Notes on Wine](#notes-on-wine) has the details that differ from CrossOver.

## If something goes differently

| What you see | What to do |
|---|---|
| `permission denied: ./citavi-crossover` | Run `zsh citavi-crossover setup` instead. |
| Terminal asks for access to your Downloads, Documents or Desktop folder, or to a removable volume | Allow it. The tool reads its own folder, and the bottle if the bottles are kept on an external disk. |
| *Citavi Login Helper* or *Citavi 7* asks for access to files on a removable volume | Allow it. This happens once when the bottle is on an external disk; the app starts Citavi from there. |
| *CrossOver is not installed* or *has never been opened*, *Wine is not installed* or *macOS does not let it start*, or *Rosetta is not installed* | Do what the message says, then run setup again. |
| A download fails | Check the internet connection and run setup again; it continues the download. |
| *the unattended .NET install did not work* | Answer `y` to the question that follows, or run `./citavi-crossover setup --guided` later. See below. |
| Citavi shows *A program error has occurred* | Click **Cancel** in the dialog. Then run `./citavi-crossover diagnose` and follow [docs/ADAPTING.md](docs/ADAPTING.md). |
| *the fixes were last checked with Citavi …, and this is another version* | Citavi's download link offers a newer Citavi than the one the fixes were written for. Often everything still works. If Citavi misbehaves, run `./citavi-crossover diagnose`. |
| CrossOver's trial has ended | Buy a CrossOver licence, or change to Wine: follow [Quick start with Wine](#quick-start-with-wine), using `CITAVI_ENGINE=wine ./citavi-crossover setup` in its step 6. Your projects in `Documents/Citavi 7` are used by both, and cloud projects come back when you sign in. |
| Anything else | `./citavi-crossover doctor` lists what is in place and names the command that repairs the rest. |

### Installing .NET through CrossOver's own window

`./citavi-crossover setup --guided` uses CrossOver's installer for .NET instead of the unattended
one. Choose it if the unattended install fails, or if you want to read Microsoft's licence terms
on screen before accepting them. The tool creates the bottle, opens CrossOver and waits. (If
CrossOver is already open, it first asks you to quit it, because an open CrossOver does not show
a new bottle. Quitting CrossOver closes the Windows programs it is running.) In CrossOver:

1. In the list on the left, click the bottle **Citavi 7**.
2. On the right, under *Bottle Actions*, click **Install Application into Bottle** (also in the
   menu: *Bottle ▸ Install Software into Bottle…*).
3. Type `.NET Framework 4.8` into the search box and choose **Microsoft .NET Framework 4.8**.
4. Click **Install**. CrossOver starts Microsoft's installers one after another (.NET 2.0, 4.0 and
   4.8). In each, accept the licence and click through to the end. If one asks to restart,
   choose to restart later.
5. When CrossOver says the installation has finished, click **Done**.

This takes about 20 minutes. The Terminal window notices when .NET 4.8 is there and carries on
with the rest of the setup by itself.

## Everyday commands

Run them in Terminal, in this folder.

| Command | What it does |
|---|---|
| `./citavi-crossover run [project.ctv6]` | Start Citavi. |
| `./citavi-crossover doctor` | Check that every fix is in place; says which command repairs what. |
| `./citavi-crossover update` | Fetch the newest Citavi installer if there is one, install it over the old version and re-apply all fixes. `update file.msi` uses an installer you downloaded yourself. |
| `./citavi-crossover uninstall` | Remove what the tool installed, part by part, asking before each. See [Removing it](#removing-it). |
| `./citavi-crossover diagnose` | Start Citavi and print its error dialog, if any, and the exceptions it threw. |
| `./citavi-crossover run --trace` | Start Citavi recording every exception; afterwards `./citavi-crossover trace` shows them. For things that fail without an error dialog. |
| `./citavi-crossover help` | Everything else. |

Settings (CrossOver or Wine, bottle name, bottle folder, Retina mode) are in `config.sh`. The
defaults follow CrossOver's own settings, so most people never edit it.

## When a new Citavi version comes out

```bash
./citavi-crossover update
```

```bash
./citavi-crossover diagnose
```

If `diagnose` reports no error dialog, you are done. If it prints a stack trace, follow
[docs/ADAPTING.md](docs/ADAPTING.md): it walks from the error to a new line in one of two data
files. If Citavi updated itself from inside the app, run `./citavi-crossover patch`.

To install from a file you downloaded yourself, give `update` the **`.msi`** installer (the
*MSI-Setup* link on [Citavi's download page](https://www.citavi.com/download)); only the `.msi`
can be installed without clicking.

### When this tool has a new version

Get the new files over the old ones, then run setup again. Setup skips what is already done and
applies what is new.

```bash
cd ~ && curl -L https://github.com/AthanasiusOfAlex/citavi-crossover/archive/refs/heads/main.tar.gz | tar xz --strip-components 1 -C citavi-crossover
```

```bash
cd ~/citavi-crossover && ./citavi-crossover setup
```

The first line replaces `config.sh` too. If you changed settings there, change them again
afterwards. The downloaded installers in `resources/` stay.

## Removing it

```bash
./citavi-crossover uninstall
```

It first says what it found. Then it asks about each part by itself, and removes a part only
after you type `y`:

| Part | What goes |
|---|---|
| The bottle | Citavi, the .NET Framework, Citavi's settings and sign-in, and the bottle's copies of your cloud projects. This cannot be undone. |
| The login helper | `Citavi Login Helper.app` in `~/Applications`, its claim on `citavi://` links, and its log in `~/Library/Logs`. |
| The app *Citavi 7* (Wine only) | `Citavi 7.app` in `~/Applications`. |
| Downloads | The installers the tool downloaded into `resources/` (about 300 MB), and `work/`. |

**It never touches your Citavi projects** in `Documents/Citavi 7`, nor CrossOver or Wine
themselves, other bottles, or this folder.

It is careful with the bottle, because a deleted bottle cannot be brought back:

- It does not delete it while Citavi is running in it.
- It does not delete it if Citavi project files lie inside the bottle itself (that is, somewhere
  on the bottle's `C:` drive and not in `Documents`). It lists them and asks you to move them
  out first.
- If setup did not make the bottle, or if the bottle holds other Windows programs or files of
  yours, it says so and asks you to type the bottle's name.
- Before you delete the bottle, start Citavi once while online and let it finish synchronising,
  so that changes you made to cloud projects while offline are not lost.

With a Citavi in CrossOver and one in Wine, run it once for each:
`CITAVI_ENGINE=crossover ./citavi-crossover uninstall` and
`CITAVI_ENGINE=wine ./citavi-crossover uninstall`.

Afterwards, move this folder to the Trash. CrossOver and Wine are separate programs; remove them
the way you installed them (move the app to the Trash, or `brew uninstall --cask wine-stable`).

**To undo only the changes to Citavi's program files** and keep everything else:

```bash
./citavi-crossover unpatch
```

It restores the original files and removes the tracer. Citavi then no longer starts in the
bottle until `./citavi-crossover patch` is run again.

**By hand,** if this folder is gone already: delete the bottle (in CrossOver's window: select
*Citavi 7*, then *Bottle ▸ Delete Bottle…*; for Wine: the folder
`~/Library/Application Support/citavi-crossover`), then `Citavi Login Helper.app` and
`Citavi 7.app` in `~/Applications`, and `CitaviLoginHelper.log` in `~/Library/Logs`.

## Notes on Wine

What is different when the tool runs with Wine and not with CrossOver:

- The bottle is a folder in `~/Library/Application Support/citavi-crossover/Bottles`. Wine calls
  such a folder a *prefix*. A bottle belongs to the program that made it: the tool refuses to use
  a CrossOver bottle with Wine, or a Wine bottle with CrossOver.
- Wine 11.0 cannot run 32-bit .NET programs on a Mac: they hang when they start. Citavi is a
  64-bit program and is not affected, but two installers use 32-bit .NET on the way. The tool
  deals with both: it stops the 32-bit half of .NET's own setup, and it takes the 32-bit steps
  out of its copy of Citavi's installer and does their work itself. One effect: Citavi's file
  types, the Word add-in and the browser pickers are not registered in the bottle. None of them
  has a use there.
- Installing .NET takes a few minutes longer. `setup --guided` does not exist, because Wine has
  no installer window of its own.
- While Citavi runs, the Dock shows it under the name *wine* with a plain icon, not as Citavi.
- Citavi's installer puts a Windows shortcut, `Citavi 7.lnk`, on the Mac's Desktop, because a
  Wine bottle uses the Mac's Desktop folder as its own. Setup removes it again.
- The icon of the app *Citavi 7* is Citavi's own, read from the installed program. It is a
  small picture enlarged, so it looks soft.

Checked with Wine Stable 11.0 on macOS 27 (November 2026): setup from scratch, signing in,
opening a cloud project, PDF preview, the word processor, text fields with Greek, Cyrillic and
Hebrew, and copy and paste. Wine Devel and Wine Staging have not been tried.

With both CrossOver and Wine on one Mac you can have a Citavi in each. They share
`~/Documents/Citavi 7`, so do not open the same local project in both at once. Sign-in links go
to one bottle at a time: `./citavi-crossover helper` points them at the bottle of the engine you
run it with.

## Display and fonts

Citavi is drawn at full Retina resolution, in Bottle UI. That is Selawik, Microsoft's open-source
stand-in for Segoe UI (the Windows font Citavi is designed around), with what it lacks added from
Open Sans and Noto Sans: the no-break space, Greek (modern and ancient), Cyrillic and Hebrew
letters, and real italics. [fonts/README.md](fonts/README.md) has the details.

On a display that is not Retina, everything is twice as large. Set `RETINA=off` in `config.sh`,
then run `./citavi-crossover registry` and restart Citavi. This is the same switch as CrossOver's
*High Resolution Mode*, so changing it there has the same effect until `registry` runs again.
Wine has the same two settings without a switch for them.

CrossOver has two settings for this, and both end up in the same two registry values. *High
Resolution Mode* switches the Mac driver's `RetinaMode` and doubles `LogPixels`; the *Screen
resolution* slider in *Wine Configuration ▸ Graphics* changes `LogPixels` alone. `RETINA=on`
writes `RetinaMode=y` and `LogPixels=192`.

Scrollbars and the other parts that Citavi's interface library (Infragistics) sizes by itself
need one more fix, the patch `infragistics-dpi-awareness`: without it they stay at half size in
Retina mode, whatever the two settings say.

## What gets changed, and where

| What | Where | Defined in |
|---|---|---|
| Microsoft .NET Framework 4.0 and 4.8, replacing the Wine Mono stand-in a new bottle comes with | the bottle | `DOTNET_STEPS` in `citavi-crossover` |
| 14 registry values: 4 that let Citavi start and preview PDFs, 10 that set its fonts | bottle registry | `patches/registry.conf` |
| 2 registry values for Retina mode | bottle registry | `RETINA` in `config.sh` |
| 10 font files (Bottle UI, built from Selawik, Open Sans and Noto Sans) | `C:\windows\Fonts` in the bottle | `fonts/` |
| 15 places in 7 assemblies (`Citavi.exe`, `SwissAcademic.dll`, `SwissAcademic.Citavi.dll`, `SwissAcademic.Controls.dll`, `SwissAcademic.Desktop.dll`, `WebSocket4Net.dll`, `Infragistics4.Win.v25.2.dll`) | Citavi's `bin` folder in the bottle | `patches/rules.json` |
| Untouched originals of patched files, the tool's own Windows programs, logs | `C:\citavi-crossover\` in the bottle | — |
| `CitaviTrace.dll`, the exception tracer (inactive unless `run --trace` or `diagnose` switches it on) | Citavi's `bin` folder in the bottle | `dev/trace/Trace.cs` |
| *Citavi Login Helper.app*, the default handler for `citavi://` links, signed for this Mac only | `~/Applications` | `login-helper/main.swift` |
| *Citavi 7.app* (Wine only), which starts Citavi; its icon is read from the installed Citavi | `~/Applications` | `login-helper/main.swift` |
| The two apps' log, a few lines per start; sign-in links are never written to it | `~/Library/Logs/CitaviLoginHelper.log` | `login-helper/main.swift` |
| Downloaded installers, kept for next time | `resources/` in this folder | — |

Each entry in the two `patches/` files carries a `why` explaining the symptom and cause.
[docs/ADAPTING.md](docs/ADAPTING.md) has the full table.

Nothing outside the bottle and this folder is modified except the two apps, their log and the
login helper's claim on `citavi://`. Citavi itself reads and writes `~/Documents/Citavi 7`,
because CrossOver and Wine map the bottle's Documents folder to yours.

### The login helper and other Citavi installations

Only one app can be the default for `citavi://`. If you also run Citavi elsewhere on this Mac
(for example in a Parallels VM, which registers the same scheme), the helper keeps that working:
when no Citavi is running in the helper's bottle, it passes the link on to the other app.
If both are running at once, the link goes to the one in the bottle.

When the helper is removed, macOS falls back to the other handler.

If you downloaded this folder with a browser, macOS marks every file in it as downloaded and
would refuse to start the helper, which no registered developer has signed. Setup therefore
installs the helper without that mark. To build it yourself from `login-helper/main.swift`
instead, see *Working on the tool*.

## Known limits

- **Cloud projects** work: opening, saving references, uploading attachments and downloading them
  again were each checked against a scratch project. Sharing a project with other people has not
  been tested.
- The **Word add-in** and the **browser Pickers** are Windows integrations and do not apply here.
- Previews that rely on Microsoft's **WebView2** runtime have not been tested; the bottle does not
  contain WebView2.
- The **News** panel on the welcome screen stays empty.
- Citavi's logo and some icons are small pictures, so Retina mode enlarges them without making
  them sharper.
- **Arabic, Chinese, Japanese and Korean are invisible in Citavi's text fields** (title, author,
  task name and the other fields you type into): each such letter is a blank gap. The text itself
  is intact. The interface font has Latin, Greek, Cyrillic and Hebrew letters, and Wine's text
  fields cannot borrow a missing letter from another font. [fonts/README.md](fonts/README.md)
  says how to add more.
- The button that opens a web address says **Open in browser**, not the browser's name. The
  button that opens a linked file, a PDF for one, says **Open in winebrowser**: that is Wine's
  helper that hands the file to the Mac's default program for it.
- The unattended .NET install leaves out **.NET Framework 2.0**, which CrossOver's own .NET
  package installs first. Citavi does not use it.
- Intel Macs, CrossOver versions other than 26.3 and Wine versions other than 11.0 have not been
  tested.

## Layout

```
citavi-crossover      the command
config.sh             settings
patches/              registry.conf and rules.json — the fixes, as data
fonts/                fonts copied into the bottle, the three fonts they are built from, and the build script
prebuilt/             the helpers, ready built: patcher, tracer, two window helpers, login helper
patcher/              source of the IL patcher (C#, Mono.Cecil); it runs inside the bottle
login-helper/         source of the citavi:// handler and of the app “Citavi 7” (Swift)
dev/                  source of the exception tracer and other helpers for investigating problems
docs/ADAPTING.md      what to do when a new version breaks
docs/WINE-DIFFERENCES.md  where Wine answers differently from Windows, measured, fix by fix
resources/            downloaded installers (not part of the project)
work/                 generated: logs, decompiled sources (safe to delete)
```

## Working on the tool

Installing and running Citavi needs no developer tools, because the helpers come ready built in
`prebuilt/`. To change one of them you need the .NET SDK (`brew install --cask dotnet-sdk`) and
the Xcode command-line tools (`xcode-select --install`); `./citavi-crossover deps` checks both and
adds the optional ILSpy and msitools. After editing a helper's source:

```bash
./citavi-crossover build
```

rebuilds `prebuilt/` and records which sources it was built from. Until then the tool refuses to
run with helpers that are older than their source. Editing `patches/registry.conf` or
`patches/rules.json` needs no build.

## Licence

This project is under the MIT licence: see [LICENSE](LICENSE). The fonts and the libraries next
to the ready-built patcher come from others and keep their own licences; they are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Citavi, CrossOver and the .NET Framework are
not part of this project.
