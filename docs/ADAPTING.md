# Adapting to a new Citavi version

Most updates need nothing more than `./citavi-crossover update`. This page is for the
times that is not enough: Citavi starts but shows *"A program error has occurred"*, shows a new
"requires …" message, does not start at all, or something in it quietly refuses to work.

The loop is always the same: **see the error → find the code → pick the smallest fix → record it
in a data file → verify.** Budget about 15–30 minutes per new problem.

## 1. Update and look

```bash
./citavi-crossover update
```

```bash
./citavi-crossover diagnose
```

`update` fetches the newest installer from Citavi's download link if it differs from the one used
last time (or takes a `.msi` you name), installs it over the old version, re-applies `patches/registry.conf` and
`patches/rules.json`, and runs `doctor`. `diagnose` starts Citavi, waits 40 seconds and prints
what it finds:

| `diagnose` says | Meaning | Go to |
|---|---|---|
| `No error dialog` | It starts. Log in, open a local and a cloud project, open a PDF preview to be sure. | done, or "Failures without an error dialog" below |
| `Citavi reported a program error:` + a stack trace | A .NET exception. The top `at …` lines name the method. | step 2 |
| A window titled `Citavi - … Required` or similar | A new prerequisite check. | step 2, search for the dialog text |
| `no Citavi window appeared` | The process died before showing anything. | read the end of `crossover.log` (with Wine: `wine.log`) in the folder `diagnose` prints |
| `doctor` warns *a patch rule no longer matches* | Citavi renamed or removed the file, method or field a rule targets. | step 2, find its new name |

Always dismiss Citavi's error dialog with **Cancel**; **OK** uploads a report to Citavi.

Errors that only happen later (after login, when opening a project…) do not show up in the first
40 seconds. Reproduce them by hand, then read the dialog: `./citavi-crossover diagnose 120` gives
you two minutes before it takes its snapshot.

### Failures without an error dialog

Some things fail with only a plain message ("A connection to the Citavi Cloud could not be
established"), or with nothing at all, because Citavi catches the exception itself. The tracer
shows those too:

```bash
./citavi-crossover run --trace
```

Reproduce the problem in Citavi, then:

```bash
./citavi-crossover trace
```

This lists every exception thrown inside Citavi, in order, including the ones it handled quietly.
`./citavi-crossover trace --full` adds the stack of each. Look for an exception that repeats or
that appears at the moment you reproduced the problem. A healthy run is not empty: expect a
`FileLoadException` for `System.Diagnostics.DiagnosticSource`, one `COMException`, and a failed
lookup of `citaviweb.blob.core.cloudapi.de` (a retired server Citavi still asks for announcements).

The tracer does not change Citavi: it is a small DLL that the .NET runtime loads when
`run --trace` or `diagnose` sets two environment variables. It never writes login tokens.

## 2. Find the code

```bash
./citavi-crossover decompile
```

This writes C# for the five main assemblies to `work/decomp/<version>/`. Search it for the
method at the top of the stack trace, or for the dialog text:

```bash
grep -rn "RegisterCustomUriScheme" work/decomp/*/ --include='*.cs'
```

Read the method and work out **which call fails under Wine**. Stack traces name the framework
method that threw (for example `System.Windows.Forms.InputLanguage.get_Culture`); the line in
Citavi's own code just above it is what you can change.

Other assemblies can be decompiled by name: `./citavi-crossover decompile SwissAcademic.Pdf.dll`.

When the failing thing is a Windows API rather than Citavi's logic, check what the bottle really
returns before guessing. `dev/probe` is a tiny .NET program for that (it needs the .NET SDK, see
*Developer helpers* below): edit `dev/probe/Program.cs` to print the value in question, then

```bash
cd dev/probe && dotnet build -c Release -o out
```

and run `out/probe.exe` in the bottle through CrossOver's *Run Command*. A probe can also test
one of Citavi's own classes in isolation: load it with `Assembly.LoadFrom` from Citavi's `bin`
folder and resolve its dependencies from the same folder in an `AppDomain.AssemblyResolve`
handler. The half-length text bug below was confirmed that way, on Citavi's `TextEditor` control.
For a bug in the order of window messages (the cut-off title on the Overview tab), a probe that
overrides `WndProc` in a small form and prints each message shows the order Wine sends them in.
For something that flickers or never settles (the quotation that kept changing its height), host
the control alone in a probe, log each of its events with the control's size, and stop after a
few rounds: the log shows which step feeds the next.

## 3. Pick the smallest fix

Prefer fixes in this order. The earlier ones survive Citavi updates on their own.

1. **Registry value** — when Citavi reads something from the registry that Wine lacks, or a
   documented switch exists (the WPF software-rendering switch is one). Add a line to
   `patches/registry.conf`, then `./citavi-crossover registry`. Besides the usual value types
   there is `FONT` for a Windows system font, written as a face name and a size in points.
2. **`redirect-call` rule** — when a *framework* method misbehaves under Wine and a sibling method
   on the same type works. This rewrites every call in every Citavi assembly, so it does not
   depend on Citavi's own method names and keeps working across versions. See how widespread
   the call is first:
   ```bash
   ./citavi-crossover find 'System.Windows.Forms.InputLanguage::get_DefaultInputLanguage'
   ```
3. **`return-constant` rule** — when one specific Citavi method must simply answer `true`, `false`,
   a number, a string or `null`, or do nothing (a `void` method). It names a Citavi type and
   method, so it can break when Citavi refactors; `doctor` tells you when that happens.

4. **`set-field-constant` rule** — when a class keeps a bad default in a field (a protocol
   version, a timeout, a flag stored as a number). It changes the integer a class stores into one
   of its own fields, wherever it does so, including the field's initial value. The field may also
   belong to a structure the class fills in before a Windows call; add `"method"` to limit the
   rule to one method (a property getter is `get_Name`).

5. **`fill-region-as-rectangles` rule** — when a control paints with `Graphics.FillRegion` on a
   surface for its whole window (`GetWindowDC`) and the far edges stay unpainted. It names an
   assembly, type and method, and turns each `FillRegion(brush, region)` there into
   `FillRectangles(brush, region.GetRegionScans(…))`, which paints the same pixels for a region
   made of rectangles. Find candidates with `./citavi-crossover find 'Graphics::FillRegion'`.

6. **`set-field-to-window-width` rule** — when a control keeps an old width although the controls
   around it follow, because its code records the width in one window message (`WM_SIZE`) and
   uses it in another that Wine sends earlier. It names an assembly, type, method and an integer
   field, and makes the method begin by storing the control's real width (`GetWindowRect`) into
   the field. It adds two small methods to the class, `CitaviCrossover_WindowWidth` and
   `CitaviCrossover_GetWindowRect`.

7. **`complete-character-placement` rule** — when text is measured wrongly, differently each time,
   by code that calls the Windows function `GetCharacterPlacement` with the flag `GCP_REORDER`.
   With that flag Wine returns the character widths but leaves the caret positions and the
   character classes unfilled, so a program that reads them gets whatever the memory held. The
   rule names the assembly, the type and the method that declare the function for the program
   (its `DllImport`), and sends every call through an added method,
   `CitaviCrossover_GetCharacterPlacement`, which fills in both after the call. Find such
   declarations with `grep -rn "GetCharacterPlacement" work/decomp/*/`.

8. **`draw-trackbar-thumb` rule** — when a slider (a standard Windows trackbar, WinForms
   `TrackBar`) has a thumb that is too small or too pale, because Wine's theme draws its own
   thumb picture. The rule names the assembly, the type of the control that holds the slider and
   the field with the slider. It gives that control a `WndProc` that answers the slider's
   custom-draw request for the thumb (`NM_CUSTOMDRAW`) and fills the thumb's rectangle with a
   pointer in Windows 10's colours, through an added method `CitaviCrossover_DrawThumb`. It is
   written for a horizontal slider with its ticks below, and it cannot be used on a control that
   already has a `WndProc` of its own. Find sliders with `grep -rln "TrackBar" work/decomp/*/`.

9. **`wide-character-call` rule** — when text that Citavi gets from a Windows function comes back
   garbled or empty, and the function exists in two forms, one for single-byte text (name ending
   in `A`) and one for 16-bit text (ending in `W`). A `DllImport` that names neither form and no
   `CharSet` calls the `A` form, and some of those misbehave under Wine. The rule names the
   assembly, the type and the method of the declaration and sets its character set to Unicode,
   so the `W` form is called. Check both forms with a probe first; `dev/probe` is a starting point.

10. **`return-constant-for-argument` rule** — when a method of Citavi asks Windows something whose
    answer in a bottle is always the same and unhelpful for one input. The rule names an assembly,
    type and method (taking a string first and returning a string), the input (`when`) and the
    answer (`value`), and makes the method begin with: if the first argument equals `when`, return
    `value`. Several such rules can name the same method with different inputs.

11. **`return-registry-value-for-argument` rule** — the same, when the answer differs from Mac to
    Mac and so cannot stand in the rule. The rule names an assembly, type and method as above and
    a registry key (`key`, written `HKEY_CURRENT_USER\…`). After the tests of the kind above, the
    method gets: if the key has a text value named like the first argument, return it. Something
    has to write the values: the names of the Mac's default apps are written by `registry`
    (`each_mac_app_fix` in `citavi-crossover`).

12. **`temporary-copy-without-asking` rule** — when Citavi asks where to save a copy of a file
    only to open or use that copy, and the question is a detour on a Mac. The rule names an
    assembly, type and method. It finds each file dialog there that is followed by `File.Copy`
    with no other dialog in between, and replaces the call that shows it by a call of an added
    method, `CitaviCrossover_TemporaryCopy`: that puts the file name Citavi proposed into
    `Citavi copies\<date>` in the bottle's folder for temporary files, deletes the folders of
    earlier days, and answers OK. The call of `SetInitialDirectoryContext` between dialog and
    copy, which remembers the dialog's folder, is taken out. `"then"` names a method that must be
    called right after the copy (`StartProcessSafe`), for a method with several such dialogs of
    which only one is to go. For an async method, name the method itself: the rule looks in the
    class the compiler made for it. Run `scan` and see that it lists exactly the places you mean.

Before writing any rule, search Citavi's code for an alternative it already contains. The cloud
fix is an example: Citavi ships a second WebSocket implementation for old Windows versions, so
the fix was one `return-constant` that selects it, not a replacement for the missing Windows
component.

Add rules to `patches/rules.json`:

```jsonc
{
  "id": "skip-some-check",                      // short, unique; shown in doctor output
  "kind": "return-constant",
  "assembly": "Citavi.exe",
  "type": "SwissAcademic.Citavi.Shell.Program", // nested types: Outer+Inner
  "method": "IsSomethingAvailable",
  "value": true,                                 // must fit the method's return type; ignored for void
  "why": "What fails under Wine and where you saw it."
}
```

```jsonc
{
  "id": "some-framework-workaround",
  "kind": "redirect-call",
  "from": "Namespace.Type::BrokenMethod",
  "to":   "Namespace.Type::WorkingMethod",      // same type, same signature
  "why": "…"
}
```

```jsonc
{
  "id": "some-better-default",
  "kind": "set-field-constant",
  "assembly": "WebSocket4Net.dll",
  "type": "WebSocket4Net.WebSocket",
  "field": "m_SecureProtocols",
  "was": 240,                                    // the integer stored today (enums are integers)
  "value": 3072,                                 // the integer to store instead
  "why": "…"
}
```

The last eight kinds each have a rule in `patches/rules.json` to copy from:
`text-field-border-fill`, `text-field-auto-height-width`, `word-processor-caret-positions`,
`task-slider-thumb`, `friendly-program-name`, `browser-name`, `mac-app-name` and
`open-copy-preview`.
The comment at the top of that file lists the properties every kind takes.

Always fill in `why` with the symptom and the cause. It is the only documentation the next
update will have.

If no rule kind can express the fix, the patcher is one file (`patcher/Program.cs`, built on
Mono.Cecil): add a new `kind` next to the existing ones in `Process()`.

## 4. Apply and verify

```bash
./citavi-crossover scan
```

```bash
./citavi-crossover patch
```

```bash
./citavi-crossover diagnose
```

`scan` shows what would change without touching anything. `patch` is safe to repeat: it only
rewrites what still needs it and keeps the untouched originals in the bottle under
`C:\citavi-crossover\backup\<version>\`. `./citavi-crossover unpatch` puts them back.

Repeat from step 1 until `diagnose` reports no error dialog, then test by hand: log in, open a
local project, open a PDF preview, and open a cloud project. For the cloud, the full check is:
create a scratch cloud project, add a reference and a PDF, quit Citavi, delete that project's
folder under `AppData\Local\Swiss Academic Software\Citavi 7\ProjectCache` in the bottle together
with its entry in `cache.info`, and reopen the project. Everything must come back.

## What is already fixed, and how it was found

| Symptom | Cause | Fix |
|---|---|---|
| "Citavi requires .NET Framework 4.8.1 or later" | `Program.IsNetFramework481OrGreaterInstalled` reads `Release` from the registry and wants ≥ 533320; CrossOver's .NET is 4.8 (528049). | registry: `dotnet-481-release`, `dotnet-481-version` |
| `NullReferenceException` in `RegistryHelper.RegisterCustomUriScheme` | `HKCU\Software\Classes` does not exist in a fresh Wine prefix. | registry: `hkcu-classes-key` |
| Login opens the browser, but Citavi never notices you signed in | The server redirects to `citavi://oauthlogincallback…`; nothing on macOS handles that scheme. | *Citavi Login Helper.app* (`login-helper/`) |
| `CultureNotFoundException … 0 (0x0000) is an invalid culture identifier` right after login | Wine's `SystemParametersInfo(SPI_GETDEFAULTINPUTLANG)` returns success without a value, so `InputLanguage.DefaultInputLanguage` has handle 0. | rule: `default-input-language` |
| PDF preview stays blank | WPF hardware rendering paints nothing under Wine. | registry: `wpf-software-rendering` |
| Cloud projects: "A connection to the Citavi Cloud could not be established. The project could not be opened." Trace: `WebSocketException: Not implemented`, then `AblyException … Error creating Socket Transport`, every 10 seconds | Citavi opens a cloud project only while its realtime channel (Ably) is connected. That channel is a WebSocket, and .NET's `ClientWebSocket` needs Windows' `websocket.dll`, which Wine only stubs. | rule: `managed-websocket-transport` makes `AblyClient.UseWebsocket4Net` return `true`, selecting the managed WebSocket library Citavi ships for Windows 7 |
| After that fix, trace: `AuthenticationException: A call to SSPI failed`, repeating | That library, WebSocket4Net, defaults to SSL 3 / TLS 1.0, which Ably's servers refuse. | rule: `managed-websocket-tls12` changes the default to TLS 1.2 |
| Text typed into a field is saved cut to half its length: a project named "CrossOver sync test" is created as "CrossOver"; a confirmation prompt shows "Cros" for "CrossOver". No exception. | `TextEditor.Text`, the getter behind many of Citavi's input fields, asks the rich-edit control for the text length with `EM_GETTEXTLENGTHEX` flag `GTL_CLOSE` and uses the answer as a buffer size in bytes. Wine answers in characters. | rule: `text-editor-full-text` changes the flag to `GTL_NUMBYTES` |
| In Retina mode the scrollbars of Citavi's lists are half as wide as they should be (17 pixels, not 34), while text and everything else are the right size. No exception. | The lists are Infragistics controls, which size their scrollbars themselves. Infragistics asks `GetProcessDpiAwareness(Process.GetCurrentProcess().Handle)` and, told "unaware", sizes for 96 dpi. Wine answers correctly only for the pseudo handle of the current process; for a real handle to the same process, which .NET passes, it says "unaware". The bottle's settings are not involved: `GetSystemMetrics(SM_CXVSCROLL)` returns 34. | rule: `infragistics-dpi-awareness` makes `NativeWindowMethods.GetProcessDpiAwarenessApi` return "system DPI aware", which is what `Citavi.exe`'s manifest declares. The file name carries Infragistics' version (`Infragistics4.Win.v25.2.dll`), so the rule's `assembly` is `Infragistics4.Win.v*.dll` |
| After switching tabs in the Reference Editor, thin strips of the previous tab stay visible along the bottom and right edge of the text fields. A forced repaint does not remove them. No exception. | Each field (`TextEditor`) clears its own 3-pixel border area with `Graphics.FillRegion` on a whole-window surface (`GetWindowDC`). Wine's GDI+ clips `FillRegion` on a window's surface to the size of the client area, so the last rows and columns are never painted. `FillRectangles` is not clipped. | rule: `text-field-border-fill` (kind `fill-region-as-rectangles`) makes `TextEditor.DrawNonClientBackground` fill the region's rectangles instead |
| On the Overview tab the title and citation stay in a narrow column and are cut off after two lines. No exception. | The field (`TextEditor` with `AutoHeight`) fits its height to its text when the rich edit control reports a new content height, and sets its width to the one it recorded in `WM_SIZE`. Wine's rich edit control re-wraps the text one message earlier (`WM_WINDOWPOSCHANGED`), so the report arrives while the recorded width is still the old one, and the field sets itself back to it. Only happens when the new width changes the number of lines. | rule: `text-field-auto-height-width` (kind `set-field-to-window-width`) makes `TextEditor.OnContentsResized` take the width from the window itself |
| On the Quotations & comments tab a quotation with formatted text flickers: its text box changes height several times a second, the text is wrapped differently each time (down to one letter per line), and the rest of the tab is not redrawn. No exception. It happens when the text has a character outside ASCII (a curly quote, a Greek word) or is set in a font with Arabic or Hebrew letters (Arial, Times New Roman). | Citavi's word processor (`WordProcessorControl`) measures such text with `GetCharacterPlacement` and the flag `GCP_REORDER`, and takes each character's width from the caret positions it returns. With that flag Wine fills in neither the caret positions nor the character classes, so the widths come from uninitialised memory and differ on every pass. In the list of quotations the box fits its height to its text and shows a scrollbar when the text does not fit, so each wrong measurement starts the next. | rule: `word-processor-caret-positions` (kind `complete-character-placement`) sends the call through an added method that fills in the caret positions (running sum of the widths) and the classes |
| On the Tasks & locations tab the slider that shows a task's state has a small white thumb with a thin grey outline; on Windows it is a solid blue pointer. No exception. | The slider is a standard Windows trackbar, drawn by the theme. Wine's theme has a white, outlined thumb picture of 13 x 21 pixels and draws it unstretched into the thumb's rectangle of 13 x 26 (Retina mode); Windows 10 fills about 17 x 27 pixels with blue. | rules: `task-slider-thumb` (the Tasks & locations tab) and `task-planner-slider-thumb` (the Status column of the Task Planner, which uses a second control for the same slider) make the task state control draw the thumb itself when the slider asks (`NM_CUSTOMDRAW`): a filled pointer the size of the thumb's rectangle, blue, or grey while the task cannot be changed or the thumb is dragged |
| Everything is blurry, each pixel drawn as four | A new bottle draws at half the Retina resolution. | `RETINA=on` in `config.sh`: `registry` sets the Mac driver's `RetinaMode` and doubles `LogPixels`, as CrossOver's *High Resolution Mode* does |
| The interface is in small Tahoma, cramped and unlike Citavi on Windows | Citavi's default font is Windows' message box font (`Environment.GetDefaultFont`), and elsewhere it asks for Segoe UI by name. A bottle reports Tahoma 8 pt and has no Segoe UI. CrossOver has no package for it; its Core Fonts are already supplied by macOS. | `fonts/` holds Bottle UI, which is Selawik (Microsoft's open-source font with Segoe UI's metrics) plus the characters it lacks; registry: `segoe-ui…` map the names, `system-font-…` set the system fonts to Segoe UI 9 pt |
| A name in a task ("@Ada Lovelace") or any other text with a no-break space has a gap more than twice as wide as a space. Greek, Cyrillic and Hebrew text in a text field is a row of such gaps. No exception. | Selawik has only Latin letters and lacks a few characters that Segoe UI has, the no-break space (U+00A0) among them. Citavi's text fields are rich edit controls; Wine's draws all text by glyph number and so cannot borrow a missing character from another font (GDI+ can; `DrawText` cannot either). It draws the font's "missing" glyph, which in Selawik is blank and 0.65 em wide (16 pixels where a space has 7). Measured with a probe in the bottle. | `fonts/build-fonts.py` builds the fonts in `fonts/` from Selawik: six characters get the glyph of the space, the hyphen or the apostrophe, and about 2600 more (Greek, modern and ancient, Cyrillic, Hebrew, º, ª and others) are copied from Open Sans and Noto Sans, scaled to Selawik's letter heights. The result is named Bottle UI, because Selawik's licence reserves its name for the unchanged font. Arabic and East Asian scripts are still missing; `fonts/README.md` says how to add a font for them |
| Italic text, such as the title on the Overview tab, is upright letters pushed over | Selawik has no italic fonts, and Wine slants the upright one where a program asks for italic. | `fonts/build-fonts.py` also builds an italic face for each weight from Noto Sans Italic, scaled the same way and given Selawik's line heights |
| The button that opens a web address or a file names the program in garbled letters ("Open in hªĺžÿo"). It works. No exception. | Citavi asks for the program's friendly name with four calls of `AssocQueryString`, the third without a buffer but with the size it just learned. Its declaration names no character set, so .NET calls `AssocQueryStringA`. Wine's answers that third call with size 0 where `AssocQueryStringW` answers with the size needed; the fourth call then gets no room and Citavi reads back what the memory held. Measured with a probe in the bottle. | rule: `friendly-program-name` makes the declaration call the 16-bit form. The name is then Wine's: "winebrowser", its helper that hands the address or file to the Mac's default program. Rule: `browser-name` makes Citavi's function answer "browser" for `.html`, which is what it asks about for every web address, so that button reads "Open in browser" |
| The button that opens a linked PDF or picture reads "Open in winebrowser". It works: the file opens in the Mac's default app. No exception. | Citavi shows the friendly name of the program that opens the file's ending. In a bottle that is winebrowser, Wine's helper that hands the file to the Mac, and Wine takes the name from the program file; it has no registry value for it. | `registry` asks the Mac for its default app for PDFs and pictures and writes the names into `HKCU\Software\citavi-crossover\Mac apps`; rule: `mac-app-name` makes Citavi's function read them there, so the button reads "Open in Preview" |
| In a cloud project "Open copy in …" first shows a "Save As" window, which starts in the folder last used (possibly one inside the bottle), and opens the PDF only after Save. No exception. | Citavi's design, on Windows too: a cloud attachment is opened as a copy, and Citavi asks where to put the copy. The code is in three places: the preview's button, the command of the attachment list, and the attachment's properties window. | rules: `open-copy-preview`, `open-copy-attachment-list` and `open-copy-attachment-window` replace the call that shows the window by an added method that names a file in `Temp\Citavi copies\<date>` and answers OK. "Save a copy as…" in the preview's Tools menu still asks |

[WINE-DIFFERENCES.md](WINE-DIFFERENCES.md) has the same faults from Wine's side: what Windows
answers and what Wine answers, measured, where the cause is in Wine's source, and which fixes a
Wine later than 11.0 no longer needs.

The installer itself was never the problem: `Citavi7Setup.msi` installs cleanly in silent mode
(`msiexec /i … /qb`, which is what `install` runs), and the interactive wizards of both the `.exe`
and the `.msi` come up normally as far as the licence page (not tested beyond that). What used to
look like a failed installation is the first row above: Citavi refusing to start afterwards.

## If Citavi updates itself

Citavi's built-in updater downloads and runs a new installer, which replaces the patched files.
The registry fixes and the login helper are unaffected. Afterwards:

```bash
./citavi-crossover doctor
```

```bash
./citavi-crossover patch
```

## Developer helpers

- Installing and running Citavi needs no developer tools: the helpers come ready built in
  `prebuilt/`. Changing one needs the .NET SDK and the Xcode command-line tools
  (`./citavi-crossover deps` checks them); `./citavi-crossover build` then rebuilds `prebuilt/` and
  rewrites `prebuilt/SOURCES.sha256`, the list of source checksums the tool compares against
  before it uses a helper. `decompile` needs `ilspycmd`, which `deps` installs.
- The patcher (`prebuilt/patcher`, source in `patcher/`) is a .NET Framework 4.8 program. The tool
  copies it and `patches/rules.json` to `C:\citavi-crossover\tools` in the bottle and runs it
  there, on the bottle's own .NET. So the paths it prints are Windows paths.
- `prebuilt/trace/CitaviTrace.dll` (source in `dev/trace`) is the exception tracer behind
  `run --trace`, `diagnose` and `trace`. Set `CITAVI_TRACE_FRAMES` to get more than 24 stack
  frames per exception.
- `prebuilt/wclick/wclick.exe` (source in `dev/wclick`) runs inside the bottle and lists or clicks
  Citavi's windows without moving your mouse: `list`, `gettext <hwnd>`, `bm <hwnd>` (press a
  button), `click <hwnd> [x y]` (click a control, in its middle unless you give a point),
  `at <top-hwnd> <x> <y>` (click at a window-relative point; `dblat` double-clicks, `rat`
  right-clicks), `key <hwnd> <vk>` (press one key in a control), `text <hwnd> <text>` (replace a
  field's text), `type <hwnd> <text>` (type into a field after `click <hwnd>` gave it the focus),
  `close <hwnd>` (close a window), `chord <top-hwnd> <vk>…` (a key combination,
  e.g. `chord <hwnd> 12 39` for Alt+9, which switches the right pane between Quick Help and the
  preview), `focus <hwnd>` (give a control the keyboard focus), `post <hwnd> <msg-hex>` (post any
  window message), `icon <program.exe> <out.png>` (write a program file's largest icon as a
  PNG; setup uses it under Wine for the icon of the app *Citavi 7*). Positions and sizes are in real pixels, so twice the on-screen points while
  Retina mode is on. Simulated clicks do not always register; `bm` and `key` are more reliable.
  `diagnose` uses it. On the Welcome Screen a project row only reacts to `bm` on its icon
  button, and the rows reorder after every opened project: check which row is which first.
- `./citavi-crossover run --log` starts Citavi with a log from CrossOver or Wine in `work/logs/`. Set `CXDEBUG`
  for Wine debug channels, e.g. `CXDEBUG=+shell,+sync ./citavi-crossover run --log`.
- The login helper's program (`prebuilt/CitaviLoginHelper`, source in `login-helper/`) serves
  two apps, both assembled by `./citavi-crossover helper`: *Citavi Login Helper*, which claims
  `citavi://`, and under Wine *Citavi 7*, which only starts Citavi. What tells them apart is in
  each app's `Info.plist` (`CitaviRole`). Both write to `~/Library/Logs/CitaviLoginHelper.log`.
- `dev/carve_pe.py` extracts the managed DLLs embedded in the installer's custom-action wrapper
  (`msiinfo extract Citavi7Setup.msi Binary.<name>` first), for when the *installer* itself fails.

## If the .NET install breaks

`setup` installs Microsoft .NET Framework 4.8 into a new bottle itself, because CrossOver's
package for it can only be installed from CrossOver's window. The steps are the table
`DOTNET_STEPS` near `cmd_dotnet` in `citavi-crossover`, copied from CrossOver's own recipe: the
entries `com.codeweavers.c4.5917` (.NET 4.0) and `com.codeweavers.c4.16255` (.NET 4.8) in
`~/Library/Application Support/CrossOver/tie/crossover.tie`. Each names an installer, the Windows
version to pretend (`installerwinver`) and DLL overrides (`installerdlloverrides`).

When a new CrossOver version makes the unattended install fail:

- Read Microsoft's log in the bottle, `users/crossover/AppData/Local/Temp/Microsoft .NET
  Framework 4… Setup_….html`. The line *Final Result* and the *BlockIf* lines above it say why an
  installer stopped. "Same or higher version … already installed" means Wine Mono's registry
  values are back: check that the Mono removal at the top of `cmd_dotnet` still finds its entry
  (`wine uninstaller --list`).
- Compare `DOTNET_STEPS` with the two catalogue entries; CodeWeavers may have changed them.
- `./citavi-crossover setup --guided` always remains: it lets CrossOver's own installer do the
  work and only waits for the result.
- The comparison that proved the unattended route: the same number of files in
  `windows/Microsoft.NET/Framework64/v4.0.30319` (437) and `Framework/v4.0.30319` (435) as in a
  bottle made by CrossOver's package, the same `mscoree.dll` in `system32` and `syswow64`
  (Microsoft's, not Wine's), `mscoree` set to `native`, and `Release` 528049 in both registry views.

## CrossOver and Wine

The tool runs Windows programs through CrossOver or through Wine from winehq.org; `ENGINE` in
`config.sh` says which. Only one part of `citavi-crossover` knows the difference, the block
headed "the engine layer". Everything else calls its functions:

| Function | With CrossOver | With Wine |
|---|---|---|
| `bwine prog …` | `wine --bottle NAME prog …` | `WINEPREFIX=<bottle folder> wine prog …` |
| `bwine -a` (an application) | `--cx-app` | nothing |
| `bwine -w` (wait for what it started) | `--wait-children` | `wineserver -w` afterwards |
| `bwine -v win7` (Windows version) | `--winver win7` | `winecfg /v win7` before, `win10` after |
| `bwine -d "fusion=b"` (DLL overrides) | `--dll` | `WINEDLLOVERRIDES` |
| `bwine -l file` (log) | `--cx-log file --debugmsg …` | `WINEDEBUG=…`, error output to the file |
| `bottle_create` | `cxbottle --create --template win10_64` | `wineboot --init` |
| `bottle_reboot` | `cxreboot` | `wineboot -r` |
| `bottle_delete` | `cxbottle --delete` | remove the folder |
| `start_citavi` | `wine --bottle NAME --cx-app Citavi.exe` | `wine Citavi.exe` in Citavi's `bin` folder |

Other differences:

- The Windows user is `crossover` in a CrossOver bottle and your Mac user name in a Wine bottle,
  so Citavi's own log and Microsoft's installer logs are under `users/<that name>/AppData`.
- A CrossOver bottle has a file `cxbottle.conf`; a Wine bottle has none. `need_own_bottle` uses
  this to refuse a bottle made by the other engine.
- With Wine, `winemenubuilder.exe` is switched off (`WINE_NOMENU`, and the same in the bottle's
  registry). It would write Linux desktop files into the home folder.
- "Is Citavi running" asks the bottle (`tasklist`), because macOS shows that a `Citavi.exe` is
  running but not in which bottle.

### 32-bit .NET under Wine

Wine 11.0 on a Mac cannot run 32-bit .NET programs. Each one hangs when it starts: no output, no
window, and it has to be stopped from outside. 64-bit .NET programs run, and Citavi and this
tool's own helpers are 64-bit. Under CrossOver 26.3 both kinds run. Three places met 32-bit
.NET, and each has its own answer in `citavi-crossover`:

| Where | What happened | What the tool does |
|---|---|---|
| Microsoft's .NET installers | They wait for the 32-bit "native image" compiler (`Framework/v4.0.30319/mscorsvw.exe`), whose worker runs at full load for ever. | `ngen32_watch` notices a worker older than 90 seconds, writes the note `logs/dotnet32-hangs` into the bottle's `C:\citavi-crossover`, and from then on stops the 32-bit compiler as it appears. `ngen32_off` then disables its service, `clr_optimization_v4.0.30319_32`. The 64-bit compiler is left alone. |
| Citavi's installer | Some of its steps (custom actions) are 32-bit .NET code. The installer hangs at `SetCustomActionData`, after all files are copied. | `msi_drop_dotnet_steps` removes those steps from the tool's copy of the installer, and `citavi_finish_install` does the part of their work that Citavi needs. See below. |
| The window helper `wclick` | It was built as a 32-bit program. | It is built as 64-bit now (`dev/wclick/wclick.csproj`). |

`dotnet32_works` decides whether any of this applies. It is true for CrossOver. For Wine it looks
for the note in the bottle, and if there is none it runs `InstallUtil.exe /?`, a small 32-bit
.NET program that comes with the .NET Framework, and gives it 30 seconds. So a later Wine that
runs 32-bit .NET gets the same plain install as CrossOver without any change here. To test again
after updating Wine, delete `logs/dotnet32-hangs`.

One more thing the .NET install needs under Wine: `C:\windows\assembly` must exist. A Wine
bottle has no such folder once Wine Mono is removed, and the native image compiler then fails
every library with "Path not found" (`ngen.log` and `ngen_service.log` in
`windows/Microsoft.NET/Framework64/v4.0.30319`) and retries without end. `cmd_dotnet` creates it.

#### Citavi's installer without its 32-bit steps

The steps are rows of the installer's `InstallExecuteSequence` table. Their code is in one
library stored inside the installer (`CustomAction` table, column `Source`).
`msi_drop_dotnet_steps` finds that library by the step `SetCustomActionData`, drops every step
that uses it, and writes the table back with `msidb`, a program that comes with Wine. The list of
dropped steps is kept in `logs/installer-steps-left-out.txt` in the bottle.

What the dropped steps do, read from their decompiled code (Citavi 7.4.1.3):

- Copy every DLL in `bin\x64` and `bin\PDFNet\x64` into `bin` (`Citavi.Copy6432Binaries`).
  Citavi does not start without these. `citavi_finish_install` does the same.
- Write `HKLM\Software\Swiss Academic Software\Citavi 7`, value `Path`, in the 32-bit registry
  view. `citavi_finish_install` does the same.
- Register the file types `.ctv6` and four more, an event log source, the Word add-in and the
  browser pickers; check for running Citavi processes; delete `.pdb` files, of which there are
  none. Not done: none of it has a use in a bottle.

If a new Citavi version changes this, `install` either stops with "could not take the 32-bit
.NET steps out of the installer" (the step `SetCustomActionData` is gone) or Citavi fails to
start for want of a library. To read the steps of a new installer:

```bash
msiinfo export Citavi7Setup.msi CustomAction
```

```bash
msiinfo extract Citavi7Setup.msi Binary.<Source of SetCustomActionData> > ca.bin
```

```bash
python3 dev/carve_pe.py ca.bin carved && ilspycmd carved/pe_2_*_managed.dll > steps.cs
```

In `ca.bin` a list of `<EntryPoint … Type="…" Method="…">` lines says which method each step
`m1`, `m2`, … runs (`strings ca.bin | grep EntryPoint`).
