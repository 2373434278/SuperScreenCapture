// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "SSCMacKit",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .library(name: "SSCMacKit", type: .dynamic, targets: ["SSCMacKit"])
    ],
    targets: [
        .target(
            name: "SSCMacKitC",
            path: "Sources/SSCMacKitC",
            publicHeadersPath: "include"
        ),
        .target(
            name: "SSCMacKit",
            dependencies: ["SSCMacKitC"],
            path: "Sources/SSCMacKit"
        )
    ]
)
