// Citavi Login Helper — forwards citavi:// URLs from macOS into the CrossOver or Wine bottle.
//
// Citavi signs in through the system browser. After you log in, the server redirects to
// citavi://oauthlogincallback#…, which Windows would hand to a second Citavi.exe that passes
// it to the running Citavi over a named pipe. On macOS nothing knows what to do with that URL,
// so this app registers itself for the citavi: scheme and starts that second Citavi.exe inside
// the bottle.
//
// If Citavi is not running in the bottle, the URL is passed on to whichever other app claims
// citavi: (for example Citavi inside a Parallels VM), so that setup keeps working.
//
// Which bottle, and whether CrossOver or Wine runs it, is written into this app's Info.plist by
// `citavi-crossover helper` (keys CitaviEngine, CitaviBottle, CitaviBottleDir, CitaviExe, and
// CitaviCrossOverRoot or CitaviWineBin).
//
// The same program is also the app "Citavi 7" that `citavi-crossover helper` makes for Wine,
// which has no app of its own for a Windows program. There the Info.plist has CitaviRole =
// launcher and no claim on citavi:, and the program starts Citavi at once.
//
// Command line (run the binary inside the bundle directly):
//   CitaviLoginHelper --register   make this app the default handler for citavi:
//   CitaviLoginHelper --status     print the current default handler for citavi:
import Cocoa

let scheme = "citavi"
let info = Bundle.main.infoDictionary ?? [:]
func cfg(_ key: String) -> String { (info[key] as? String) ?? "" }

func currentHandler() -> URL? {
    NSWorkspace.shared.urlForApplication(toOpen: URL(string: "\(scheme)://x")!)
}

func otherHandlers() -> [URL] {
    let me = Bundle.main.bundleURL.standardizedFileURL
    return NSWorkspace.shared.urlsForApplications(toOpen: URL(string: "\(scheme)://x")!)
        .filter { $0.standardizedFileURL != me }
}

func log(_ msg: String) {
    let url = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/CitaviLoginHelper.log")
    let line = "\(ISO8601DateFormatter().string(from: Date())) \(msg)\n"
    if let h = try? FileHandle(forWritingTo: url) {
        h.seekToEndOfFile(); h.write(line.data(using: .utf8)!); h.closeFile()
    } else {
        try? line.write(to: url, atomically: true, encoding: .utf8)
    }
}

let engine = cfg("CitaviEngine") == "wine" ? "wine" : "crossover"
let launcher = cfg("CitaviRole") == "launcher"
let engineName = engine == "wine" ? "Wine" : "CrossOver"
let winePath = engine == "wine" ? cfg("CitaviWineBin") + "/wine" : cfg("CitaviCrossOverRoot") + "/bin/wine"

/// A process that runs a Windows program in the bottle. `app` marks a program given by its
/// Windows path, which CrossOver wants to be told.
func wineProcess(_ arguments: [String], app: Bool) -> Process {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: winePath)
    var env = ProcessInfo.processInfo.environment
    if engine == "wine" {
        p.arguments = arguments
        env["WINEPREFIX"] = cfg("CitaviBottleDir") + "/" + cfg("CitaviBottle")
        env["WINEDEBUG"] = "-all"
        env["WINEDLLOVERRIDES"] = "winemenubuilder.exe=d"
        let dir = cfg("CitaviBinDir")
        if !dir.isEmpty { p.currentDirectoryURL = URL(fileURLWithPath: dir) }
    } else {
        p.arguments = ["--bottle", cfg("CitaviBottle")] + (app ? ["--cx-app"] : []) + arguments
        if !cfg("CitaviBottleDir").isEmpty { env["CX_BOTTLE_PATH"] = cfg("CitaviBottleDir") }
    }
    p.environment = env
    return p
}

/// True when a Citavi.exe is running in this app's bottle. A Citavi in another bottle does not
/// count, and one in a Parallels VM is not visible from here at all.
func citaviRunningInBottle() -> Bool {
    // Cheap first: is there any Citavi.exe under Wine on this Mac?
    let any = Process()
    any.executableURL = URL(fileURLWithPath: "/usr/bin/pgrep")
    any.arguments = ["-f", "Citavi\\.exe"]
    any.standardOutput = FileHandle.nullDevice
    any.standardError = FileHandle.nullDevice
    do { try any.run() } catch { return false }
    any.waitUntilExit()
    if any.terminationStatus != 0 { return false }
    // Then ask the bottle itself for its list of running programs.
    let list = wineProcess(["tasklist"], app: false)
    let pipe = Pipe()
    list.standardOutput = pipe
    list.standardError = FileHandle.nullDevice
    do { try list.run() } catch { return false }
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    list.waitUntilExit()
    return String(decoding: data, as: UTF8.self).lowercased().contains("citavi.exe")
}

// ---- command-line modes ------------------------------------------------------------------

let args = CommandLine.arguments.dropFirst()
if args.contains("--status") {
    print(currentHandler()?.path ?? "(none)")
    exit(0)
}
if args.contains("--register") {
    let sem = DispatchSemaphore(value: 0)
    var failure: Error?
    NSWorkspace.shared.setDefaultApplication(at: Bundle.main.bundleURL, toOpenURLsWithScheme: scheme) { err in
        failure = err; sem.signal()
    }
    _ = sem.wait(timeout: .now() + 10)
    if let failure { FileHandle.standardError.write("could not register: \(failure.localizedDescription)\n".data(using: .utf8)!); exit(1) }
    print(currentHandler()?.path ?? "(none)")
    exit(0)
}

// ---- app mode ----------------------------------------------------------------------------

final class Delegate: NSObject, NSApplicationDelegate {
    var handled = false

    func runWine(_ extra: [String]) {
        let p = wineProcess([cfg("CitaviExe")] + extra, app: true)
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch {
            log("failed to start wine: \(error)")
            let a = NSAlert()
            a.messageText = launcher ? "Citavi 7" : "Citavi Login Helper"
            a.informativeText = "Could not start \(engineName)'s wine at \(winePath):\n\(error.localizedDescription)\n\nRe-run “citavi-crossover helper” to rebuild this app."
            a.runModal()
        }
    }

    func applicationWillFinishLaunching(_ n: Notification) {
        NSAppleEventManager.shared().setEventHandler(self, andSelector: #selector(handleURL(_:reply:)),
            forEventClass: AEEventClass(kInternetEventClass), andEventID: AEEventID(kAEGetURL))
    }

    func applicationDidFinishLaunching(_ n: Notification) {
        // Opened by hand (no URL arrives): just start Citavi. The login helper waits a moment
        // for a URL first; the "Citavi 7" app never gets one.
        DispatchQueue.main.asyncAfter(deadline: .now() + (launcher ? 0 : 1.5)) {
            if !self.handled { log(launcher ? "starting Citavi" : "opened without a URL; starting Citavi"); self.runWine([]) }
            DispatchQueue.main.asyncAfter(deadline: .now() + 2) { NSApp.terminate(nil) }
        }
    }

    @objc func handleURL(_ event: NSAppleEventDescriptor, reply: NSAppleEventDescriptor) {
        handled = true
        guard let text = event.paramDescriptor(forKeyword: keyDirectObject)?.stringValue,
              text.lowercased().hasPrefix("\(scheme):") else { log("ignored a non-citavi URL"); return }
        // Never log the URL itself: it carries the login tokens.
        if citaviRunningInBottle() {
            log("forwarding citavi URL (\(text.count) chars) to bottle '\(cfg("CitaviBottle"))'")
            runWine([text])
        } else if let other = otherHandlers().first, let url = URL(string: text) {
            log("Citavi is not running in the \(engineName) bottle; passing the URL on to \(other.lastPathComponent)")
            NSWorkspace.shared.open([url], withApplicationAt: other, configuration: NSWorkspace.OpenConfiguration())
        } else {
            log("Citavi is not running in the \(engineName) bottle and no other handler exists; starting it with the URL")
            runWine([text])
        }
    }
}

let app = NSApplication.shared
let delegate = Delegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
