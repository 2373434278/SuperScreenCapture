import AVFoundation
import CoreMedia
import Foundation
import ScreenCaptureKit
import SSCMacKitC

private let recordingLock = NSLock()
private var activeRecorder: RecordingSession?
private var lastRecorder: RecordingSession?

private enum RecordingStateCode: Int32 {
    case idle = 0
    case recording = 1
    case paused = 2
    case finalizing = 3
    case completed = 4
    case failed = 5
}

@_cdecl("ssc_microphone_permission_status")
public func ssc_microphone_permission_status() -> Int32 {
    switch AVCaptureDevice.authorizationStatus(for: .audio) {
    case .authorized:
        return 1
    case .denied:
        return 2
    case .restricted:
        return 3
    case .notDetermined:
        return 0
    @unknown default:
        return 0
    }
}

@_cdecl("ssc_request_microphone_permission")
public func ssc_request_microphone_permission() -> Int32 {
    let status = AVCaptureDevice.authorizationStatus(for: .audio)
    if status == .authorized {
        return 1
    }
    if status == .denied || status == .restricted {
        return 2
    }

    let semaphore = DispatchSemaphore(value: 0)
    var granted = false
    AVCaptureDevice.requestAccess(for: .audio) { allowed in
        granted = allowed
        semaphore.signal()
    }
    semaphore.wait()
    return granted ? 1 : 2
}

@_cdecl("ssc_recording_start")
public func ssc_recording_start(
    _ path: UnsafePointer<CChar>?,
    _ config: UnsafePointer<ssc_recording_config>?
) -> Int32 {
    guard let path, let config else {
        setLastError("Recording path or configuration was null.")
        return 0
    }

    recordingLock.lock()
    if activeRecorder != nil {
        recordingLock.unlock()
        setLastError("A recording is already in progress.")
        return 0
    }

    lastRecorder = nil
    let outputPath = String(cString: path)
    let recorder: RecordingSession
    do {
        recorder = try RecordingSession(path: outputPath, config: config.pointee)
    } catch {
        recordingLock.unlock()
        setLastError(error.localizedDescription)
        return 0
    }

    activeRecorder = recorder
    recordingLock.unlock()

    let semaphore = DispatchSemaphore(value: 0)
    var startError: Error?
    Task {
        do {
            try await recorder.start()
        } catch {
            startError = error
        }
        semaphore.signal()
    }
    semaphore.wait()

    if let startError {
        recorder.abort()
        recordingLock.lock()
        if activeRecorder === recorder {
            activeRecorder = nil
        }
        recordingLock.unlock()
        setLastError(startError.localizedDescription)
        return 0
    }

    return 1
}

@_cdecl("ssc_recording_pause")
public func ssc_recording_pause() -> Int32 {
    currentRecorder()?.pause() == true ? 1 : 0
}

@_cdecl("ssc_recording_resume")
public func ssc_recording_resume() -> Int32 {
    currentRecorder()?.resume() == true ? 1 : 0
}

@_cdecl("ssc_recording_stop")
public func ssc_recording_stop() -> Int32 {
    guard let recorder = currentRecorder() else {
        setLastError("No recording is active.")
        return 0
    }

    let success = recorder.stop()
    let finalState = recorder.stateCode
    recordingLock.lock()
    if activeRecorder === recorder && (success || finalState == RecordingStateCode.completed.rawValue ||
        finalState == RecordingStateCode.failed.rawValue) {
        activeRecorder = nil
        lastRecorder = recorder
    }
    recordingLock.unlock()
    return success ? 1 : 0
}

@_cdecl("ssc_recording_set_microphone")
public func ssc_recording_set_microphone(_ enabled: Int32) -> Int32 {
    currentRecorder()?.setMicrophone(enabled != 0) == true ? 1 : 0
}

@_cdecl("ssc_recording_state")
public func ssc_recording_state() -> Int32 {
    statusRecorder()?.stateCode ?? RecordingStateCode.idle.rawValue
}

@_cdecl("ssc_recording_elapsed_seconds")
public func ssc_recording_elapsed_seconds() -> Double {
    statusRecorder()?.elapsedSeconds ?? 0
}

@_cdecl("ssc_recording_mic_level")
public func ssc_recording_mic_level() -> Double {
    statusRecorder()?.microphoneLevel ?? 0
}

private func currentRecorder() -> RecordingSession? {
    recordingLock.lock()
    defer { recordingLock.unlock() }
    return activeRecorder
}

private func statusRecorder() -> RecordingSession? {
    recordingLock.lock()
    defer { recordingLock.unlock() }
    return activeRecorder ?? lastRecorder
}

private final class RecordingSession: NSObject, SCStreamOutput, SCStreamDelegate,
    AVCaptureAudioDataOutputSampleBufferDelegate
{
    private let outputPath: String
    private let config: ssc_recording_config
    private let writer: AVAssetWriter
    private let videoInput: AVAssetWriterInput
    private let pixelAdaptor: AVAssetWriterInputPixelBufferAdaptor
    private let audioInput: AVAssetWriterInput?
    private let videoQueue = DispatchQueue(label: "dev.superscreencapture.recording.video")
    private let audioQueue = DispatchQueue(label: "dev.superscreencapture.recording.audio")
    private let stateLock = NSLock()
    private var stream: SCStream?
    private var audioSession: AVCaptureSession?
    private var state: RecordingStateCode = .recording
    private var startPTS: CMTime?
    private var pauseBeganHostTime: CMTime?
    private var pauseOffset: CMTime = .zero
    private var elapsed: Double = 0
    private var level: Double = 0
    private var microphoneEnabled: Bool
    private var aborted = false

    init(path: String, config: ssc_recording_config) throws {
        self.outputPath = path
        self.config = config
        self.microphoneEnabled = config.include_microphone != 0

        let url = URL(fileURLWithPath: path)
        if FileManager.default.fileExists(atPath: path) {
            try FileManager.default.removeItem(at: url)
        }

        writer = try AVAssetWriter(outputURL: url, fileType: .mp4)
        writer.shouldOptimizeForNetworkUse = true

        let bitrate = max(1, Int(config.bitrate_mbps)) * 1_000_000
        let videoSettings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: Int(config.output_width),
            AVVideoHeightKey: Int(config.output_height),
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: bitrate,
                AVVideoExpectedSourceFrameRateKey: Int(config.fps),
                AVVideoMaxKeyFrameIntervalKey: max(1, Int(config.fps) * 2),
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel
            ]
        ]

        videoInput = AVAssetWriterInput(mediaType: .video, outputSettings: videoSettings)
        videoInput.expectsMediaDataInRealTime = true
        pixelAdaptor = AVAssetWriterInputPixelBufferAdaptor(
            assetWriterInput: videoInput,
            sourcePixelBufferAttributes: [
                kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                kCVPixelBufferWidthKey as String: Int(config.output_width),
                kCVPixelBufferHeightKey as String: Int(config.output_height),
                kCVPixelBufferIOSurfacePropertiesKey as String: [:] as CFDictionary
            ])
        guard writer.canAdd(videoInput) else {
            throw NSError(
                domain: "SSCMacKit",
                code: -10,
                userInfo: [NSLocalizedDescriptionKey: "Unable to add the video writer input."])
        }
        writer.add(videoInput)

        if config.include_microphone != 0 {
            let audioSettings: [String: Any] = [
                AVFormatIDKey: kAudioFormatMPEG4AAC,
                AVSampleRateKey: 48_000,
                AVNumberOfChannelsKey: 1,
                AVEncoderBitRateKey: 128_000
            ]
            let input = AVAssetWriterInput(mediaType: .audio, outputSettings: audioSettings)
            input.expectsMediaDataInRealTime = true
            guard writer.canAdd(input) else {
                throw NSError(
                    domain: "SSCMacKit",
                    code: -11,
                    userInfo: [NSLocalizedDescriptionKey: "Unable to add the audio writer input."])
            }
            writer.add(input)
            audioInput = input
        } else {
            audioInput = nil
        }

        guard writer.startWriting() else {
            throw writer.error ?? NSError(
                domain: "SSCMacKit",
                code: -12,
                userInfo: [NSLocalizedDescriptionKey: "Unable to start the video writer."])
        }
        writer.startSession(atSourceTime: .zero)

        super.init()
    }

    var stateCode: Int32 {
        stateLock.lock()
        defer { stateLock.unlock() }
        return state.rawValue
    }

    var elapsedSeconds: Double {
        stateLock.lock()
        defer { stateLock.unlock() }
        return elapsed
    }

    var microphoneLevel: Double {
        stateLock.lock()
        defer { stateLock.unlock() }
        return level
    }

    func start() async throws {
        let content = try await SCShareableContent.current
        guard let display = content.displays.first(where: { $0.displayID == config.display_id }) else {
            throw NSError(
                domain: "SSCMacKit",
                code: -13,
                userInfo: [NSLocalizedDescriptionKey: "Display \(config.display_id) is not available for recording."])
        }

        let scale = config.display_scale > 0 ? config.display_scale : 1
        let displayFrame = display.frame
        let region = CGRect(
            x: (CGFloat(config.x) / CGFloat(scale)) - displayFrame.origin.x,
            y: (CGFloat(config.y) / CGFloat(scale)) - displayFrame.origin.y,
            width: CGFloat(config.width) / CGFloat(scale),
            height: CGFloat(config.height) / CGFloat(scale))
        let sourceRect = region.intersection(CGRect(origin: .zero, size: displayFrame.size))
        guard !sourceRect.isEmpty else {
            throw NSError(
                domain: "SSCMacKit",
                code: -14,
                userInfo: [NSLocalizedDescriptionKey: "Recording region is outside the display."])
        }

        let filter = SCContentFilter(display: display, excludingWindows: ownApplicationWindows(in: content))
        let streamConfiguration = SCStreamConfiguration()
        streamConfiguration.sourceRect = sourceRect
        streamConfiguration.width = Int(config.output_width)
        streamConfiguration.height = Int(config.output_height)
        streamConfiguration.minimumFrameInterval = CMTime(
            value: 1,
            timescale: CMTimeScale(max(1, Int(config.fps))))
        streamConfiguration.showsCursor = true
        streamConfiguration.capturesAudio = false
        streamConfiguration.pixelFormat = kCVPixelFormatType_32BGRA
        streamConfiguration.queueDepth = 6

        let stream = SCStream(
            filter: filter,
            configuration: streamConfiguration,
            delegate: self)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: videoQueue)
        self.stream = stream
        try await stream.startCapture()

        if config.include_microphone != 0 {
            try startAudioCapture()
        }

        markRecording()
    }

    private func markRecording() {
        stateLock.lock()
        state = .recording
        stateLock.unlock()
    }

    func pause() -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        guard state == .recording else {
            return false
        }

        state = .paused
        pauseBeganHostTime = hostTime()
        return true
    }

    func resume() -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        guard state == .paused else {
            return false
        }

        if let pauseBeganHostTime {
            pauseOffset = CMTimeAdd(pauseOffset, CMTimeSubtract(hostTime(), pauseBeganHostTime))
        }
        self.pauseBeganHostTime = nil
        state = .recording
        return true
    }

    func setMicrophone(_ enabled: Bool) -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        guard audioInput != nil else {
            return false
        }

        microphoneEnabled = enabled
        if !enabled {
            level = 0
        }
        return true
    }

    func stop() -> Bool {
        stateLock.lock()
        if state == .completed || state == .failed {
            let completed = state == .completed
            stateLock.unlock()
            return completed
        }
        guard state == .recording || state == .paused else {
            stateLock.unlock()
            return false
        }
        state = .finalizing
        stateLock.unlock()

        stopStreamAndAudio()
        videoInput.markAsFinished()
        audioInput?.markAsFinished()

        let semaphore = DispatchSemaphore(value: 0)
        writer.finishWriting {
            semaphore.signal()
        }
        semaphore.wait()

        stateLock.lock()
        if writer.status == .completed {
            state = .completed
            level = 0
            stateLock.unlock()
            return true
        }

        state = .failed
        level = 0
        stateLock.unlock()
        setLastError(writer.error?.localizedDescription ?? "Unable to finalize the recording.")
        return false
    }

    func abort() {
        stateLock.lock()
        if aborted {
            stateLock.unlock()
            return
        }
        aborted = true
        state = .failed
        stateLock.unlock()

        stopStreamAndAudio()
        if writer.status == .writing {
            writer.cancelWriting()
        }
        try? FileManager.default.removeItem(atPath: outputPath)
    }

    func stream(
        _ stream: SCStream,
        didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
        of type: SCStreamOutputType
    ) {
        guard type == .screen, sampleBuffer.isValid else {
            return
        }

        guard let attachments = CMSampleBufferGetSampleAttachmentsArray(
            sampleBuffer,
            createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
            let statusValue = attachments.first?[.status] as? Int,
            statusValue == SCFrameStatus.complete.rawValue else {
            return
        }

        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else {
            return
        }

        let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        stateLock.lock()
        guard state == .recording else {
            stateLock.unlock()
            return
        }

        let origin = startPTS ?? pts
        if startPTS == nil {
            startPTS = pts
        }
        let adjusted = CMTimeSubtract(CMTimeSubtract(pts, origin), pauseOffset)
        stateLock.unlock()

        guard adjusted.isValid, CMTimeCompare(adjusted, .zero) >= 0 else {
            return
        }

        if pixelAdaptor.append(pixelBuffer, withPresentationTime: adjusted) {
            stateLock.lock()
            elapsed = max(elapsed, CMTimeGetSeconds(adjusted))
            stateLock.unlock()
        }
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        fail(error)
    }

    func captureOutput(
        _ output: AVCaptureOutput,
        didOutput sampleBuffer: CMSampleBuffer,
        from connection: AVCaptureConnection
    ) {
        guard let audioInput, sampleBuffer.isValid else {
            return
        }

        let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        stateLock.lock()
        guard state == .recording, microphoneEnabled, let origin = startPTS else {
            stateLock.unlock()
            return
        }
        let adjusted = CMTimeSubtract(CMTimeSubtract(pts, origin), pauseOffset)
        let measured = calculateLevel(sampleBuffer)
        level = measured
        stateLock.unlock()

        guard adjusted.isValid, CMTimeCompare(adjusted, .zero) >= 0 else {
            return
        }

        guard let retimed = retimed(sampleBuffer, presentationTime: adjusted) else {
            return
        }

        audioInput.append(retimed)
    }

    private func startAudioCapture() throws {
        guard AVCaptureDevice.authorizationStatus(for: .audio) == .authorized else {
            throw NSError(
                domain: "SSCMacKit",
                code: -15,
                userInfo: [NSLocalizedDescriptionKey: "Microphone permission is required."])
        }

        guard let device = AVCaptureDevice.default(for: .audio) else {
            throw NSError(
                domain: "SSCMacKit",
                code: -16,
                userInfo: [NSLocalizedDescriptionKey: "No microphone is available."])
        }

        let input = try AVCaptureDeviceInput(device: device)
        let session = AVCaptureSession()
        session.beginConfiguration()
        guard session.canAddInput(input) else {
            session.commitConfiguration()
            throw NSError(
                domain: "SSCMacKit",
                code: -17,
                userInfo: [NSLocalizedDescriptionKey: "Unable to add the microphone input."])
        }
        session.addInput(input)

        let output = AVCaptureAudioDataOutput()
        output.audioSettings = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVSampleRateKey: 48_000,
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
            AVLinearPCMIsNonInterleaved: false
        ]
        output.setSampleBufferDelegate(self, queue: audioQueue)
        guard session.canAddOutput(output) else {
            session.commitConfiguration()
            throw NSError(
                domain: "SSCMacKit",
                code: -18,
                userInfo: [NSLocalizedDescriptionKey: "Unable to add the microphone output."])
        }
        session.addOutput(output)
        session.commitConfiguration()
        session.startRunning()
        audioSession = session
    }

    private func stopStreamAndAudio() {
        if let stream {
            let semaphore = DispatchSemaphore(value: 0)
            stream.stopCapture { _ in
                semaphore.signal()
            }
            semaphore.wait()
            self.stream = nil
        }

        audioSession?.stopRunning()
        audioSession = nil
    }

    private func fail(_ error: Error) {
        stateLock.lock()
        if state == .failed || state == .completed {
            stateLock.unlock()
            return
        }
        state = .failed
        level = 0
        stateLock.unlock()

        setLastError(error.localizedDescription)
        audioSession?.stopRunning()
        audioSession = nil
        self.stream = nil
        if writer.status == .writing {
            writer.cancelWriting()
        }
        try? FileManager.default.removeItem(atPath: outputPath)
    }

    private func hostTime() -> CMTime {
        CMClockGetTime(CMClockGetHostTimeClock())
    }

    private func retimed(_ sample: CMSampleBuffer, presentationTime: CMTime) -> CMSampleBuffer? {
        var timingCount = 0
        CMSampleBufferGetSampleTimingInfoArray(
            sample,
            entryCount: 0,
            arrayToFill: nil,
            entriesNeededOut: &timingCount)
        guard timingCount > 0 else {
            return nil
        }

        var timings = [CMSampleTimingInfo](repeating: CMSampleTimingInfo(), count: timingCount)
        let status = CMSampleBufferGetSampleTimingInfoArray(
            sample,
            entryCount: timingCount,
            arrayToFill: &timings,
            entriesNeededOut: &timingCount)
        guard status == noErr else {
            return nil
        }

        let delta = CMTimeSubtract(presentationTime, CMSampleBufferGetPresentationTimeStamp(sample))
        for index in 0..<timingCount {
            timings[index].presentationTimeStamp = CMTimeAdd(
                timings[index].presentationTimeStamp,
                delta)
        }

        var output: CMSampleBuffer?
        let copyStatus = CMSampleBufferCreateCopyWithNewTiming(
            allocator: kCFAllocatorDefault,
            sampleBuffer: sample,
            sampleTimingEntryCount: timingCount,
            sampleTimingArray: &timings,
            sampleBufferOut: &output)
        return copyStatus == noErr ? output : nil
    }

    private func calculateLevel(_ sample: CMSampleBuffer) -> Double {
        var bufferList = AudioBufferList()
        var blockBuffer: CMBlockBuffer?
        let status = CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(
            sample,
            bufferListSizeNeededOut: nil,
            bufferListOut: &bufferList,
            bufferListSize: MemoryLayout<AudioBufferList>.size,
            blockBufferAllocator: kCFAllocatorDefault,
            blockBufferMemoryAllocator: kCFAllocatorDefault,
            flags: kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment,
            blockBufferOut: &blockBuffer)
        guard status == noErr else {
            return 0
        }

        var sum = 0.0
        var count = 0
        let buffers = UnsafeMutableAudioBufferListPointer(&bufferList)
        for buffer in buffers {
            guard let data = buffer.mData else {
                continue
            }

            let samples = data.assumingMemoryBound(to: Int16.self)
            let sampleCount = Int(buffer.mDataByteSize) / MemoryLayout<Int16>.size
            for index in 0..<sampleCount {
                let value = Double(samples[index]) / 32_768.0
                sum += value * value
                count += 1
            }
        }

        guard count > 0 else {
            return 0
        }

        return min(1.0, sqrt(sum / Double(count)) * 6.0)
    }
}
