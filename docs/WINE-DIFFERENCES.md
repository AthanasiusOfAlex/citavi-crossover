# Where Wine differs from Windows

Every fix in this project exists because Wine answers a program differently from Windows. This
page records each difference from Wine's side: the Windows function, what Windows answers, what
Wine answers, and where in Wine's source the cause is. Its use is to decide, after a new version
of Wine or CrossOver, **whether a fix is still needed**.

[ADAPTING.md](ADAPTING.md) tells the same story from Citavi's side, symptom by symptom, in the
table "What is already fixed, and how it was found".

## How it was measured

On 2026-11-18 one small test program called each function on three systems:

- **CrossOver 26.3.0** (build 39832) on macOS 27.2, Apple Silicon, in a 64-bit Windows 10 bottle
  with Microsoft .NET Framework 4.8 and High Resolution Mode on (192 dpi). Its Wine calls itself
  11.0.
- **Wine Stable 11.0** from winehq.org, in a new bottle with .NET Framework 4.8.
- **Windows 11 Pro**, version 25H2 (build 26200.9818), in Parallels Desktop. On Apple Silicon
  that is Windows on ARM; the x64 test program ran under Windows' own x64 emulation.

Both builds of Wine gave the same answers, so the tables below say "Wine 11.0" for both.
"Current Wine" means the `master` branch on 2026-11-18 (version 11.22). Its source was read; it
was not built or run.

To repeat a measurement, put the call into `dev/probe/Program.cs` (see ADAPTING.md, step 2) and
run it in the bottle.

## Still different in current Wine

| Fix in `patches/rules.json` | Function | Wine 11.0 | Windows |
|---|---|---|---|
| `word-processor-caret-positions` | `GetCharacterPlacementW` with `GCP_REORDER` | `lpCaretPos` and `lpClass` untouched | both filled |
| `text-field-border-fill` | GDI+ `FillRegion` on a `GetWindowDC` surface | stops at 290 x 73 of 300 x 80 | whole window |
| `friendly-program-name` | `AssocQueryStringA` without a buffer | `S_OK`, then size 0 | `S_FALSE`, size 16 |
| `text-editor-full-text` | `EM_GETTEXTLENGTHEX` with `GTL_CLOSE` | 19 (characters) | 38 (bytes) |
| `text-field-auto-height-width` | `EN_REQUESTRESIZE` and `WM_SIZE` | notification before `WM_SIZE` | notification inside `WM_SIZE` |

### 1. gdi32: `GetCharacterPlacementW` with `GCP_REORDER`

Called with `GCP_REORDER` (alone or with `GCP_GLYPHSHAPE`) and result buffers for `lpDx`,
`lpCaretPos` and `lpClass`, Wine fills `lpDx` and `lpOrder` and returns success, but writes
nothing to `lpCaretPos` and `lpClass`. The caller reads whatever the memory held.

Measured with every buffer preset to `CC` bytes, the text `“Obj 1:”` (with U+201C and U+201D)
and Arial at 24 pixels, flags `GCP_REORDER`:

```
Wine 11.0   lpDx       8 13 D 5 7 D 7 8
            lpCaretPos CCCCCCCC CCCCCCCC CCCCCCCC …
            lpClass    CC CC CC CC CC CC CC CC
Windows     lpDx       8 13 E 6 7 D 7 8
            lpCaretPos 0 8 1B 29 2F 36 43 4A
            lpClass    01 01 01 01 01 01 01 01
```

On Windows `lpCaretPos` is the running sum of `lpDx`, and `lpClass` is 1 (`GCPCLASS_LATIN`) for
each character, with every combination of flags. Windows also fills `lpClass` without
`GCP_REORDER`; Wine does not. The fix in this project fills both the way Windows does for text
without right-to-left characters.

**In Wine's source:** `GetCharacterPlacementW` in `dlls/gdi32/text.c` prints two FIXMEs,
"classes not implemented" and "Caret positions for complex scripts not implemented", and fills
`lpCaretPos` only under `if (result->lpCaretPos && !(flags & GCP_REORDER))`. `lpClass` is never
written.

**`GetFontLanguageInfo` makes it more frequent.** Citavi's word processor takes the
`GCP_REORDER` path when the text has a character outside ASCII, or when `GetFontLanguageInfo`
reports `GCP_GLYPHSHAPE`, `GCP_LIGATE` or `GCP_REORDER` for the font:

| Font | Wine 11.0 | Windows |
|---|---|---|
| Arial | `0x18` (`GCP_USEKERNING`, `GCP_GLYPHSHAPE`) | `0x40008` (`FLI_GLYPHS`, `GCP_USEKERNING`) |
| Segoe UI | `0x0` (a stand-in font; the bottle has no Segoe UI) | `0x40008` |

Wine sets `GCP_GLYPHSHAPE` whenever the font's code page signature has the Arabic bit
(`GCP_GLYPHSHAPE_MASK = FS_ARABIC`) and never sets `FLI_GLYPHS`. So under Wine all text in Arial
goes down that path; on Windows plain text in Arial does not. This part is less sure than the
rest: the two systems do not use the same Arial file, and only one character set was tested.

**Without the fix:** on the Quotations & comments tab a quotation with formatted text changes
its height several times a second.

### 2. gdiplus: `FillRegion` on a surface for the whole window

A window has a non-client frame. The program takes a surface for the whole window with
`GetWindowDC`, makes a GDI+ `Graphics` from it and fills the frame with `Graphics.FillRegion`.
Under Wine the fill stops at the client area's width and height, counted from the window's
corner. `FillRectangle` and `FillRectangles` on the same `Graphics` reach the whole window, on
both systems.

Measured with a window of 300 x 80 and a client area of 291 x 74 at (3, 3): `FillRegion` paints
columns 0 to 289 and rows 0 to 72 under Wine, and all 300 x 80 on Windows.

**In Wine's source:** `get_graphics_device_bounds` in `dlls/gdiplus/graphics.c` uses
`GetClientRect(graphics->hwnd)` whenever the `Graphics` belongs to a window, also when its
surface covers the whole window. `get_visible_clip_region` intersects with those bounds, and
`GDI32_GdipFillRegion` clips with `get_clipped_region_hrgn`. The calls between these functions
were not all traced, so take the cause as likely, not proven. Why the fill ends one pixel short
of the client size is not known.

**Without the fix:** after a tab switch, thin strips of the previous tab stay along the bottom
and right edge of every text field.

### 3. shlwapi: `AssocQueryStringA` without an output buffer

With `pszOut` NULL, the W form returns `S_FALSE` and the needed size on both systems. Wine's A
form returns `S_OK`, and when `*pcchOut` is not 0 on that call it sets the size to 0.

Measured with `ASSOCF_VERIFY`, `ASSOCSTR_FRIENDLYAPPNAME` and `".txt"`, four calls in a row,
each given the size the call before it returned:

```
                           Wine 11.0                    Windows
1) out NULL, size 0     -> 0x0, size 8                  0x1, size 16
2) out buffer           -> 0x0, size 8, "notepad"       0x0, size 8, "Notepad"
3) out NULL, size 8     -> 0x0, size 0                  0x1, size 16
4) out buffer           -> 0x0, buffer not written      0x0, size 8, "Notepad"
```

**In Wine's source:** `AssocQueryStringA` in `dlls/shlwapi/assoc.c` calls the W form with a
buffer of its own, then converts with `WideCharToMultiByte(…, pszOut, *pcchOut, …)` and stores
the result in `*pcchOut`. With `pszOut` NULL and `*pcchOut` not 0 that conversion fails and
returns 0, and the function's result stays `S_OK`.

**Without the fix:** the button that opens a web address or a file reads "Open in hªĺžÿo".

### 4. riched20: `EM_GETTEXTLENGTHEX` with `GTL_CLOSE` alone

Measured on a `RICHEDIT50W` window with the 19 characters "CrossOver sync test", code page 1200:

| Flags | Wine 11.0 | Windows |
|---|---|---|
| `GTL_DEFAULT` | 19 | 19 |
| `GTL_PRECISE` | 38 | 38 |
| `GTL_CLOSE` | **19** | **38** |
| `GTL_NUMCHARS` | 19 | 19 |
| `GTL_NUMBYTES` | 38 | 38 |
| `GTL_CLOSE \| GTL_NUMCHARS` | 19 | 19 |
| `GTL_CLOSE \| GTL_NUMBYTES` | 38 | 38 |

Microsoft's documentation does not settle which is right: it calls `GTL_CLOSE` a close
approximation that "can be used to set the buffer size" and does not name the unit. Citavi uses
the answer as a count of bytes.

**In Wine's source:** `ME_GetTextLengthEx` in `dlls/riched20/caret.c` returns bytes only for
`GTL_NUMBYTES`, or for `GTL_PRECISE` without `GTL_NUMCHARS`.

**Without the fix:** every text read from a text field comes back cut to half. A project named
"CrossOver sync test" is created as "CrossOver".

### 5. riched20: `EN_REQUESTRESIZE` arrives before `WM_SIZE`

Measured by widening a multi-line rich edit box with several lines of text from 200 to 900
pixels:

```
Wine 11.0                              Windows
WM_WINDOWPOSCHANGED {                  WM_WINDOWPOSCHANGED {
  EN_REQUESTRESIZE                       WM_SIZE {
  WM_SIZE { }                              EN_REQUESTRESIZE
}                                        }
                                       }
```

**In Wine's source:** the window procedure in `dlls/riched20/txthost.c` handles
`WM_WINDOWPOSCHANGED` itself: it stores the new client rectangle and calls
`ITextServices_OnTxPropertyBitsChange(TXTBIT_CLIENTRECTCHANGE)`, which re-wraps the text, before
`DefWindowProcW` sends `WM_SIZE`.

**Without the fix:** the title on the Overview tab stays in a narrow column.

## Fixed in Wine after 11.0

CrossOver 26.3 and Wine Stable 11.0 still need these three fixes. An engine built on a later
Wine should not. Each fix gives Citavi the answer Windows gives, so leaving it in does no harm.

| Fix | Wine 11.0 | Windows | Current Wine |
|---|---|---|---|
| `default-input-language` (rule) | `SystemParametersInfoW(SPI_GETDEFAULTINPUTLANG)` returns TRUE and does not write the layout. .NET's `InputLanguage.DefaultInputLanguage` then has handle 0, and Citavi fails right after login. | Writes the layout, the same value as `GetKeyboardLayout(0)`. | The `SPI_GETDEFAULTINPUTLANG` case in `dlls/win32u/sysparams.c` writes `NtUserGetKeyboardLayout(0)`. |
| `hkcu-classes-key` (registry) | A new bottle has no `HKEY_CURRENT_USER\Software\Classes`: neither `loader/wine.inf.in` of Wine 11.0 nor CrossOver's `share/wine/wine.inf` has a line for it. Citavi opens the key at startup and stops with a `NullReferenceException`. | The key exists. | `loader/wine.inf.in` has the line `HKCU,Software\Classes,,16`. |
| `infragistics-dpi-awareness` (rule) | `GetProcessDpiAwareness` answers "unaware" (0) for a real handle to the own process and 1 for the pseudo handle. .NET passes a real handle, so Infragistics sizes scrollbars for 96 dpi. | Answers 1 for both handles. | `NtUserGetProcessDpiAwarenessContext` compares the handle with the current process using `NtCompareObjects`. |

## Gaps that stay worked around

Not checked against current Wine, and not measured with a test program.

- **`websocket.dll` is a stub.** .NET's `ClientWebSocket` throws "Not implemented" from
  `WebSocketProtocolComponent`. Fix: `managed-websocket-transport`.
- **WPF with hardware acceleration paints nothing.** Citavi's PDF preview is a WPF control.
  Fix: `wpf-software-rendering`, which sets
  `HKCU\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration = 1`.
- **Rich edit controls do not borrow missing characters from another font.** A character the
  chosen font lacks is drawn as a blank gap as wide as the font's "missing" glyph. GDI+ text in
  the same program does fall back. This is why the fonts in `fonts/` carry Greek, Cyrillic and
  Hebrew themselves.
- **The themed trackbar thumb is small.** The theme's picture for the thumb is 13 x 21 pixels,
  white with a grey outline, drawn unstretched in a 13 x 26 rectangle at 192 dpi. Fixes:
  `task-slider-thumb` and `task-planner-slider-thumb`.
- **CrossOver's .NET package installs 4.8.** Citavi 7.4 and later refuse to start unless the
  registry reports .NET Framework 4.8.1 (`Release` 533320 or more). Citavi runs on 4.8 once the
  registry says 4.8.1. Fixes: `dotnet-481-release` and `dotnet-481-version`.
- **Wine 11.0 on an Apple Silicon Mac hangs 32-bit .NET Framework programs** when they start;
  64-bit ones run, and CrossOver 26.3 runs both. Not tested with a later Wine.
  [ADAPTING.md](ADAPTING.md) describes it under "32-bit .NET under Wine".

## Not Wine's doing

- `managed-websocket-tls12`: the WebSocket library that Citavi ships as a fallback defaults to
  SSL 3 and TLS 1.0. That is Citavi's code.
- `browser-name`: a choice of wording for a button.
- The fonts: Segoe UI is proprietary, so no bottle has it.

## Wine bugs near these faults

Found on 2026-11-18 in the archive of the wine-bugs mailing list. None describes the same fault
as a section above, and their present status was not looked up. They are the places to watch
for a change in Wine.

| Section | Bugs at bugs.winehq.org |
|---|---|
| 1 | 7183 (an application whose menus vanish, with the FIXME "classes not implemented"), 45793 (`lpOrder` with `GCP_REORDER`), 25085 (asks to remove that FIXME), 57368 (invisible text, another flag) |
| 3 | 59402 and merge request 10072 (the W form, with a buffer) |
| 2, 4, 5 | none found |
| Citavi itself | 55869, "Citavi 6.17 … fails to launch on Wine 8.18, Mac OS on Apple Silicon" |
