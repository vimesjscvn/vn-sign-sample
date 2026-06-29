// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "UsbTokenAgent",
    platforms: [.macOS(.v14)],
    targets: [
        .target(
            name: "CPkcs11",
            path: "CPkcs11",
            publicHeadersPath: "include"
        ),
        .executableTarget(
            name: "UsbTokenAgent",
            dependencies: ["CPkcs11"],
            path: "Sources",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Security"),
            ]
        ),
    ]
)
