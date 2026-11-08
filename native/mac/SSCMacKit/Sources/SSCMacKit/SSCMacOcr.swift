import CoreGraphics
import Foundation
import Vision
import SSCMacKitC

@_cdecl("ssc_ocr_image")
public func ssc_ocr_image(
    _ pixels: UnsafePointer<UInt8>?,
    _ width: UInt32,
    _ height: UInt32,
    _ stride: UInt32,
    _ languages: UnsafePointer<CChar>?,
    _ outJson: UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?
) -> Int32 {
    guard let pixels, width > 0, height > 0, stride >= width * 4, let outJson else {
        setLastError("Invalid OCR image.")
        return 0
    }

    do {
        let byteCount = Int(stride) * Int(height)
        let data = Data(bytes: pixels, count: byteCount)
        guard let provider = CGDataProvider(data: data as CFData) else {
            throw NSError(
                domain: "SSCMacKit",
                code: -20,
                userInfo: [NSLocalizedDescriptionKey: "Unable to create the OCR image provider."])
        }

        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let bitmapInfo = CGBitmapInfo(rawValue:
            CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        guard let image = CGImage(
            width: Int(width),
            height: Int(height),
            bitsPerComponent: 8,
            bitsPerPixel: 32,
            bytesPerRow: Int(stride),
            space: colorSpace,
            bitmapInfo: bitmapInfo,
            provider: provider,
            decode: nil,
            shouldInterpolate: false,
            intent: .defaultIntent
        ) else {
            throw NSError(
                domain: "SSCMacKit",
                code: -21,
                userInfo: [NSLocalizedDescriptionKey: "Unable to create the OCR image."])
        }

        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        if let languages {
            let list = String(cString: languages)
                .split(separator: ",")
                .map { $0.trimmingCharacters(in: .whitespaces) }
                .filter { !$0.isEmpty }
            if !list.isEmpty {
                request.recognitionLanguages = list
            }
        }

        try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])

        var lines: [[String: Any]] = []
        var textParts: [String] = []
        for observation in request.results ?? [] {
            guard let candidate = observation.topCandidates(1).first else {
                continue
            }

            let box = observation.boundingBox
            let x = Int((box.minX * CGFloat(width)).rounded())
            let y = Int(((1 - box.maxY) * CGFloat(height)).rounded())
            let lineWidth = max(1, Int((box.width * CGFloat(width)).rounded()))
            let lineHeight = max(1, Int((box.height * CGFloat(height)).rounded()))
            lines.append([
                "text": candidate.string,
                "x": x,
                "y": y,
                "width": lineWidth,
                "height": lineHeight,
                "confidence": Double(candidate.confidence)
            ])
            textParts.append(candidate.string)
        }

        let payload: [String: Any] = [
            "text": textParts.joined(separator: "\n"),
            "language": (languages.map { String(cString: $0) } ?? "zh-Hans,en-US"),
            "lines": lines
        ]
        let json = try JSONSerialization.data(withJSONObject: payload, options: [])
        guard let text = String(data: json, encoding: .utf8) else {
            throw NSError(
                domain: "SSCMacKit",
                code: -22,
                userInfo: [NSLocalizedDescriptionKey: "Unable to encode OCR output."])
        }

        outJson.pointee = strdup(text)
        return 1
    } catch {
        setLastError(error.localizedDescription)
        return 0
    }
}

@_cdecl("ssc_free_string")
public func ssc_free_string(_ value: UnsafeMutablePointer<CChar>?) {
    free(value)
}
