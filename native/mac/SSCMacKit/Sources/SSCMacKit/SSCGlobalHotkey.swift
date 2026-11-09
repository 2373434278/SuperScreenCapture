import Carbon.HIToolbox
import Foundation

public typealias SSCHotkeyCallback = @convention(c) (UInt32, UnsafeMutableRawPointer?) -> Void

private let hotkeyAlreadyExistsStatus: OSStatus = -9878

private struct SSCHotkeyEntry {
    let ref: EventHotKeyRef
    let callback: SSCHotkeyCallback
    let userData: UnsafeMutableRawPointer?
}

private func sscHotkeyEventHandler(
    _ nextHandler: EventHandlerCallRef?,
    _ event: EventRef?,
    _ userData: UnsafeMutableRawPointer?
) -> OSStatus {
    guard let event else { return OSStatus(eventNotHandledErr) }

    var hotKeyID = EventHotKeyID()
    let status = GetEventParameter(
        event,
        EventParamName(kEventParamDirectObject),
        EventParamType(typeEventHotKeyID),
        nil,
        MemoryLayout<EventHotKeyID>.size,
        nil,
        &hotKeyID)
    guard status == noErr else { return status }
    return SSCHotkeyRegistry.shared.dispatch(id: hotKeyID.id)
}

private final class SSCHotkeyRegistry {
    static let shared = SSCHotkeyRegistry()

    private let lock = NSLock()
    private var entries: [UInt32: SSCHotkeyEntry] = [:]
    private var handler: EventHandlerRef?
    private var handlerInstalled = false

    func register(
        id: UInt32,
        keyCode: UInt32,
        modifiers: UInt32,
        callback: SSCHotkeyCallback,
        userData: UnsafeMutableRawPointer?
    ) -> OSStatus {
        installHandlerIfNeeded()
        unregister(id: id)

        var ref: EventHotKeyRef?
        let hotKeyID = EventHotKeyID(signature: OSType(0x53534348), id: id)
        let status = RegisterEventHotKey(keyCode, modifiers, hotKeyID, GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let ref else {
            if status == hotkeyAlreadyExistsStatus {
                setLastError("The shortcut is already in use by another application.")
            } else {
                setLastError("Unable to register the shortcut (status \(status)).")
            }
            return status
        }

        lock.lock()
        entries[id] = SSCHotkeyEntry(ref: ref, callback: callback, userData: userData)
        lock.unlock()
        return noErr
    }

    func unregister(id: UInt32) {
        lock.lock()
        let entry = entries.removeValue(forKey: id)
        lock.unlock()
        if let entry {
            UnregisterEventHotKey(entry.ref)
        }
    }

    func unregisterAll() {
        lock.lock()
        let snapshot = entries
        entries.removeAll()
        lock.unlock()
        for entry in snapshot.values {
            UnregisterEventHotKey(entry.ref)
        }
    }

    func dispatch(id: UInt32) -> OSStatus {
        lock.lock()
        let entry = entries[id]
        lock.unlock()
        entry?.callback(id, entry?.userData)
        return noErr
    }

    private func installHandlerIfNeeded() {
        guard !handlerInstalled else { return }
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed))
        let status = InstallEventHandler(
            GetApplicationEventTarget(),
            sscHotkeyEventHandler,
            1,
            &eventType,
            nil,
            &handler)
        if status == noErr {
            handlerInstalled = true
        } else {
            setLastError("Unable to install the hotkey event handler (status \(status)).")
        }
    }
}

@_cdecl("ssc_hotkey_register")
public func ssc_hotkey_register(
    _ id: UInt32,
    _ keyCode: UInt32,
    _ modifiers: UInt32,
    _ callback: SSCHotkeyCallback?,
    _ userData: UnsafeMutableRawPointer?
) -> Int32 {
    guard Thread.isMainThread else {
        setLastError("Global hotkeys must be registered on the main thread.")
        return -1
    }
    guard let callback else {
        setLastError("Hotkey callback was null.")
        return -1
    }
    return SSCHotkeyRegistry.shared.register(
        id: id,
        keyCode: keyCode,
        modifiers: modifiers,
        callback: callback,
        userData: userData)
}

@_cdecl("ssc_hotkey_unregister")
public func ssc_hotkey_unregister(_ id: UInt32) -> Int32 {
    SSCHotkeyRegistry.shared.unregister(id: id)
    return 0
}

@_cdecl("ssc_hotkey_unregister_all")
public func ssc_hotkey_unregister_all() {
    SSCHotkeyRegistry.shared.unregisterAll()
}
