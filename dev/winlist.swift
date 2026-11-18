import CoreGraphics
import Foundation
let opts: CGWindowListOption = [.optionAll]
let list = CGWindowListCopyWindowInfo(opts, kCGNullWindowID) as! [[String: Any]]
for w in list {
    let owner = w[kCGWindowOwnerName as String] as? String ?? ""
    let name = w[kCGWindowName as String] as? String ?? ""
    let id = w[kCGWindowNumber as String] as? Int ?? 0
    let layer = w[kCGWindowLayer as String] as? Int ?? 0
    let onscreen = w[kCGWindowIsOnscreen as String] as? Bool ?? false
    let b = w[kCGWindowBounds as String] as? [String: Any] ?? [:]
    let filter = CommandLine.arguments.count > 1 ? CommandLine.arguments[1].lowercased() : ""
    if filter.isEmpty || owner.lowercased().contains(filter) {
        print("\(id)\t\(owner)\t\(name)\tlayer=\(layer)\ton=\(onscreen)\t\(b["X"] ?? 0),\(b["Y"] ?? 0) \(b["Width"] ?? 0)x\(b["Height"] ?? 0)")
    }
}
