import Foundation
import ServiceManagement

@_cdecl("ssc_autostart_status")
public func ssc_autostart_status() -> Int32 {
    guard Bundle.main.bundleIdentifier != nil, Bundle.main.bundlePath.hasSuffix(".app") else {
        return 3
    }

    if #available(macOS 13.0, *) {
        switch SMAppService.mainApp.status {
        case .notRegistered:
            return 0
        case .enabled:
            return 1
        case .requiresApproval:
            return 2
        case .notFound:
            return 3
        @unknown default:
            return 3
        }
    }

    return 3
}

@_cdecl("ssc_autostart_set")
public func ssc_autostart_set(_ enabled: Int32) -> Int32 {
    guard Bundle.main.bundleIdentifier != nil, Bundle.main.bundlePath.hasSuffix(".app") else {
        setLastError("Launch at login requires a bundled application.")
        return 3
    }

    if #available(macOS 13.0, *) {
        do {
            if enabled != 0 {
                if SMAppService.mainApp.status != .enabled {
                    try SMAppService.mainApp.register()
                }
            } else if SMAppService.mainApp.status != .notRegistered {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            setLastError(error.localizedDescription)
            return 4
        }

        return ssc_autostart_status()
    }

    return 3
}
