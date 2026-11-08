import AppKit
import CoreGraphics
import Foundation
import ScreenCaptureKit
import SSCMacKitC

private let errorBuffer = UnsafeMutablePointer<CChar>.allocate(capacity: 2048)
private let errorLock = NSLock()

private func setLastError(_ message: String) {
    errorLock.lock()
    defer { errorLock.unlock() }
    errorBuffer.initialize(repeating: 0, count: 2048)
    let bytes = Array(message.utf8.prefix(2047))
    for (index, byte) in bytes.enumerated() {
        errorBuffer[index] = CChar(bitPattern: byte)
    }
}

@_cdecl("ssc_last_error")
public func ssc_last_error() -> UnsafePointer<CChar>? {
    errorLock.lock()
    defer { errorLock.unlock() }
    return UnsafePointer(errorBuffer)
}

@_cdecl("ssc_free_image")
public func ssc_free_image(_ image: UnsafeMutablePointer<ssc_image>?) {
    guard let image else { return }
    image.pointee.pixels?.deallocate()
    image.pointee.pixels = nil
    image.pointee.width = 0
    image.pointee.height = 0
    image.pointee.stride = 0
}

@_cdecl("ssc_preflight_permission")
public func ssc_preflight_permission() -> Int32 {
    CGPreflightScreenCaptureAccess() ? 1 : 0
}

@_cdecl("ssc_request_permission")
public func ssc_request_permission() -> Int32 {
    CGRequestScreenCaptureAccess() ? 1 : 0
}

private func activeDisplayIds() -> [CGDirectDisplayID] {
    var count: UInt32 = 0
    guard CGGetActiveDisplayList(0, nil, &count) == .success, count > 0 else {
        setLastError("Unable to enumerate displays.")
        return []
    }

    var ids = [CGDirectDisplayID](repeating: 0, count: Int(count))
    var actual: UInt32 = 0
    guard CGGetActiveDisplayList(count, &ids, &actual) == .success else {
        setLastError("Unable to read display list.")
        return []
    }

    return Array(ids.prefix(Int(actual)))
}

@_cdecl("ssc_display_count")
public func ssc_display_count() -> Int32 {
    Int32(activeDisplayIds().count)
}

@_cdecl("ssc_display_get")
public func ssc_display_get(_ index: Int32, _ out: UnsafeMutablePointer<ssc_display>?) -> Int32 {
    guard let out else {
        setLastError("Display output pointer was null.")
        return 0
    }

    let ids = activeDisplayIds()
    guard index >= 0, Int(index) < ids.count else {
        setLastError("Display index \(index) is out of range.")
        return 0
    }

    let displayId = ids[Int(index)]
    let bounds = CGDisplayBounds(displayId)
    let pixelWidth = CGDisplayPixelsWide(displayId)
    let pixelHeight = CGDisplayPixelsHigh(displayId)
    let scale = bounds.width > 0 ? Double(pixelWidth) / Double(bounds.width) : 1.0

    out.pointee.id = UInt32(displayId)
    out.pointee.x = Int32((bounds.origin.x * scale).rounded())
    out.pointee.y = Int32((bounds.origin.y * scale).rounded())
    out.pointee.width = UInt32(pixelWidth)
    out.pointee.height = UInt32(pixelHeight)
    out.pointee.scale = scale
    out.pointee.primary = CGDisplayIsMain(displayId) != 0 ? 1 : 0
    return 1
}

@_cdecl("ssc_window_at_point")
public func ssc_window_at_point(_ x: Int32, _ y: Int32, _ out: UnsafeMutablePointer<ssc_window>?) -> Int32 {
    guard let out else {
        setLastError("Window output pointer was null.")
        return 0
    }

    let point = CGPoint(x: CGFloat(x), y: CGFloat(y))
    let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
    guard let windows = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else {
        setLastError("Unable to enumerate windows.")
        return 0
    }

    for window in windows {
        guard let layer = window[kCGWindowLayer as String] as? Int, layer == 0 else {
            continue
        }

        guard let bounds = window[kCGWindowBounds as String] as? [String: Any],
              let rect = CGRect(dictionaryRepresentation: bounds as CFDictionary),
              rect.contains(point) else {
            continue
        }

        let owner = window[kCGWindowOwnerName as String] as? String ?? ""
        if owner == "SuperScreenCapture" || owner == "SSC.App" {
            continue
        }

        guard let number = window[kCGWindowNumber as String] as? Int else {
            continue
        }

        out.pointee.id = UInt32(number)
        out.pointee.x = Int32(rect.origin.x.rounded())
        out.pointee.y = Int32(rect.origin.y.rounded())
        out.pointee.width = UInt32(rect.width.rounded())
        out.pointee.height = UInt32(rect.height.rounded())
        return 1
    }

    setLastError("No capturable window found at the pointer location.")
    return 0
}

private func captureImageSync(
    filter: SCContentFilter,
    configuration: SCStreamConfiguration
) throws -> CGImage {
    let semaphore = DispatchSemaphore(value: 0)
    var result: Result<CGImage, Error> = .failure(NSError(
        domain: "SSCMacKit",
        code: -1,
        userInfo: [NSLocalizedDescriptionKey: "Capture did not start."]
    ))

    Task {
        do {
            let image = try await SCScreenshotManager.captureImage(
                contentFilter: filter,
                configuration: configuration
            )
            result = .success(image)
        } catch {
            result = .failure(error)
        }
        semaphore.signal()
    }

    semaphore.wait()
    return try result.get()
}

private func shareableContentSync() throws -> SCShareableContent {
    let semaphore = DispatchSemaphore(value: 0)
    var result: Result<SCShareableContent, Error> = .failure(NSError(
        domain: "SSCMacKit",
        code: -2,
        userInfo: [NSLocalizedDescriptionKey: "ScreenCaptureKit did not start."]
    ))

    Task {
        do {
            result = .success(try await SCShareableContent.current)
        } catch {
            result = .failure(error)
        }
        semaphore.signal()
    }

    semaphore.wait()
    return try result.get()
}

private func imageToNative(_ image: CGImage) throws -> ssc_image {
    let width = image.width
    let height = image.height
    let stride = width * 4
    guard let pixels = UnsafeMutablePointer<UInt8>.allocate(capacity: stride * height) as UnsafeMutablePointer<UInt8>? else {
        throw NSError(domain: "SSCMacKit", code: -3, userInfo: [NSLocalizedDescriptionKey: "Unable to allocate image buffer."])
    }
    pixels.initialize(repeating: 0, count: stride * height)

    let colorSpace = CGColorSpaceCreateDeviceRGB()
    let bitmapInfo = CGBitmapInfo(rawValue:
        CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)

    guard let context = CGContext(
        data: pixels,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: stride,
        space: colorSpace,
        bitmapInfo: bitmapInfo.rawValue
    ) else {
        pixels.deallocate()
        throw NSError(domain: "SSCMacKit", code: -4, userInfo: [NSLocalizedDescriptionKey: "Unable to create bitmap context."])
    }

    context.setBlendMode(.copy)
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    return ssc_image(
        width: UInt32(width),
        height: UInt32(height),
        stride: UInt32(stride),
        pixels: pixels
    )
}

@_cdecl("ssc_capture_display")
public func ssc_capture_display(_ displayId: UInt32, _ out: UnsafeMutablePointer<ssc_image>?) -> Int32 {
    guard let out else {
        setLastError("Image output pointer was null.")
        return 0
    }

    do {
        let content = try shareableContentSync()
        guard let display = content.displays.first(where: { $0.displayID == displayId }) else {
            setLastError("Display \(displayId) is not available for capture.")
            return 0
        }

        let filter = SCContentFilter(display: display, excludingWindows: [])
        let configuration = SCStreamConfiguration()
        configuration.width = display.width
        configuration.height = display.height
        configuration.showsCursor = true
        configuration.capturesAudio = false
        configuration.pixelFormat = kCVPixelFormatType_32BGRA

        let image = try captureImageSync(filter: filter, configuration: configuration)
        out.pointee = try imageToNative(image)
        return 1
    } catch {
        setLastError(error.localizedDescription)
        return 0
    }
}

@_cdecl("ssc_capture_window")
public func ssc_capture_window(_ windowId: UInt32, _ out: UnsafeMutablePointer<ssc_image>?) -> Int32 {
    guard let out else {
        setLastError("Image output pointer was null.")
        return 0
    }

    do {
        let content = try shareableContentSync()
        guard let window = content.windows.first(where: { $0.windowID == windowId }) else {
            setLastError("Window \(windowId) is not available for capture.")
            return 0
        }

        let filter = SCContentFilter(desktopIndependentWindow: window)
        let scale = NSScreen.main?.backingScaleFactor ?? 2.0
        let configuration = SCStreamConfiguration()
        configuration.width = max(1, Int((window.frame.width * scale).rounded()))
        configuration.height = max(1, Int((window.frame.height * scale).rounded()))
        configuration.showsCursor = false
        configuration.capturesAudio = false
        configuration.pixelFormat = kCVPixelFormatType_32BGRA

        let image = try captureImageSync(filter: filter, configuration: configuration)
        out.pointee = try imageToNative(image)
        return 1
    } catch {
        setLastError(error.localizedDescription)
        return 0
    }
}

@_cdecl("ssc_copy_image_to_clipboard")
public func ssc_copy_image_to_clipboard(
    _ pixels: UnsafePointer<UInt8>?,
    _ width: UInt32,
    _ height: UInt32,
    _ stride: UInt32
) -> Int32 {
    guard let pixels, width > 0, height > 0, stride >= width * 4 else {
        setLastError("Invalid clipboard image.")
        return 0
    }

    let byteCount = Int(stride) * Int(height)
    guard let copy = UnsafeMutablePointer<UInt8>.allocate(capacity: byteCount) as UnsafeMutablePointer<UInt8>? else {
        setLastError("Unable to allocate clipboard buffer.")
        return 0
    }
    copy.initialize(from: pixels, count: byteCount)

    let colorSpace = CGColorSpaceCreateDeviceRGB()
    let bitmapInfo = CGBitmapInfo(rawValue:
        CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)

    guard let context = CGContext(
        data: copy,
        width: Int(width),
        height: Int(height),
        bitsPerComponent: 8,
        bytesPerRow: Int(stride),
        space: colorSpace,
        bitmapInfo: bitmapInfo.rawValue
    ), let image = context.makeImage() else {
        copy.deallocate()
        setLastError("Unable to create clipboard image.")
        return 0
    }

    let nsImage = NSImage(cgImage: image, size: NSSize(width: CGFloat(width), height: CGFloat(height)))
    let pasteboard = NSPasteboard.general
    pasteboard.clearContents()
    let written = pasteboard.writeObjects([nsImage])
    copy.deallocate()

    if !written {
        setLastError("Unable to write image to the pasteboard.")
        return 0
    }

    return 1
}
