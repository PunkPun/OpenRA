/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */

import Foundation

let SYSTEM_MONO_PATH = "/Library/Frameworks/Mono.framework/Versions/Current/"
let SYSTEM_MONO_MIN_VERSION = "6.12"
let DOTNET_MIN_MACOS_VERSION: Double = 10.15

typealias MonoMain = @convention(c) (Int32, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>) -> Int32

typealias HostfxrHandle = UnsafeMutableRawPointer
struct HostfxrInitializeParameters {
    let size: Int32
    let hostPath: UnsafeMutablePointer<CChar>?
    let dotnetRoot: UnsafeMutablePointer<CChar>?
}

typealias HostfxrInitializeForDotnetCommandLineFn = @convention(c) (Int32, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>, UnsafeMutablePointer<HostfxrInitializeParameters>, UnsafeMutablePointer<UnsafeMutableRawPointer?>) -> Int32
typealias HostfxrRunAppFn = @convention(c) (HostfxrHandle) -> Int32
typealias HostfxrCloseFn = @convention(c) (HostfxrHandle) -> Int32

func launch_mono(args: [String], modId: String)
{
    let exePath = Bundle.main.bundlePath.appending("/Contents/MacOS/")
    let hostPath = SYSTEM_MONO_PATH.appending("lib/libmonosgen-2.0.dylib")
    let dllPath = exePath.appending("mono/OpenRA.Utility.dll")
    
    var limit = rlimit()
    if getrlimit(RLIMIT_NOFILE, &limit) == 0 && limit.rlim_cur < 1024 {
        limit.rlim_cur = min(limit.rlim_max, 1024)
        setrlimit(RLIMIT_NOFILE, &limit)
    }

    guard let libmono = dlopen(hostPath, RTLD_LAZY) else
    {
        NSLog("Failed to load libmonosgen-2.0.dylib: %s\n", dlerror()!)
        exit(1)
    }

    guard let monoMain = dlsym(libmono, "mono_main").map({ unsafeBitCast($0, to: MonoMain.self) }) else
    {
        NSLog("Could not load mono_main(): %s\n", dlerror()!)
        exit(1)
    }
    
    // Create an array of C strings
    var cStrings: [UnsafeMutablePointer<CChar>?] = []
    
    cStrings.append(strdup(hostPath))
    cStrings.append(strdup(dllPath))
    cStrings.append(strdup(modId))
    
    for arg in args
    {
        cStrings.append(strdup(arg))
    }
    
    // Ensure the memory is cleaned up after the function returns
    defer
    {
        for cString in cStrings
        {
            if let cString = cString
            {
                free(cString)
            }
        }
    }
    
    // Allocate the argument list as a contiguous block of memory
    let argc = Int32(cStrings.count)
    let argv = UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>.allocate(capacity: cStrings.count)
    
    // Copy the C string pointers into the argv array
    for (index, cString) in cStrings.enumerated()
    {
        argv[index] = cString
    }
    
    defer
    {
        argv.deallocate()
    }
    
    exit(monoMain(argc, argv))
}

func launch_dotnet(args: [String], modId: String, isArmArchitecture: Bool)
{
    let exePath = Bundle.main.bundlePath.appending("/Contents/MacOS/")
    let hostPath: String
    let dllPath: String
    
    if isArmArchitecture {
        hostPath = exePath.appending("arm64/libhostfxr.dylib")
        dllPath = exePath.appending("arm64/OpenRA.Utility.dll")
    } else {
        hostPath = exePath.appending("x86_64/libhostfxr.dylib")
        dllPath = exePath.appending("x86_64/OpenRA.Utility.dll")
    }

    guard let lib = dlopen(hostPath, RTLD_LAZY) else {
        NSLog("Failed to load \(hostPath): %s\n", dlerror()!)
        exit(1)
    }

    guard let hostfxrInitializeForDotnetCommandLine = dlsym(lib, "hostfxr_initialize_for_dotnet_command_line").map({ unsafeBitCast($0, to: HostfxrInitializeForDotnetCommandLineFn.self) }) else {
        NSLog("Could not load hostfxr_initialize_for_dotnet_command_line(): %s\n", dlerror()!)
        exit(1)
    }

    guard let hostfxrRunApp = dlsym(lib, "hostfxr_run_app").map({ unsafeBitCast($0, to: HostfxrRunAppFn.self) }) else {
        NSLog("Could not load hostfxr_run_app(): %s\n", dlerror()!)
        exit(1)
    }

    guard let hostfxrClose = dlsym(lib, "hostfxr_close").map({ unsafeBitCast($0, to: HostfxrCloseFn.self) }) else {
        NSLog("Could not load hostfxr_close(): %s\n", dlerror()!)
        exit(1)
    }

    // Create an array of C strings
    var cStrings: [UnsafeMutablePointer<CChar>?] = [
        strdup(hostPath),
        strdup(dllPath),
        strdup(modId)
    ]
    
    cStrings.append(contentsOf: args.map { strdup($0) })
    
    // Ensure the memory is cleaned up after the function returns
    defer {
        cStrings.forEach { free($0) }
    }
    
    // Allocate the argument list as a contiguous block of memory
    let argc = Int32(cStrings.count)
    let argv = UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>.allocate(capacity: cStrings.count)
    
    // Copy the C string pointers into the argv array
    for (index, cString) in cStrings.enumerated() {
        argv[index] = cString
    }
    
    // Ensure argv is deallocated after use
    defer {
        argv.deallocate()
    }

    var params = HostfxrInitializeParameters(
        size: Int32(MemoryLayout<HostfxrInitializeParameters>.size),
        hostPath: strdup(exePath.appending("Utility")),
        dotnetRoot: strdup(dirname(strdup(hostPath)))
    )

    // Ensure params are cleaned up after use
    defer {
        free(params.hostPath)
        free(params.dotnetRoot)
    }

    var hostContextHandle: HostfxrHandle?
    
    // Call the hostfxr_initialize_for_dotnet_command_line function
    let initResult = hostfxrInitializeForDotnetCommandLine(argc, argv, &params, &hostContextHandle)
    if initResult != 0 {
        exit(1)
    }
    
    // Call the hostfxr_run_app function
    let runResult = hostfxrRunApp(hostContextHandle)
    
    // Call the hostfxr_close function
    let closeResult = hostfxrClose(hostContextHandle)
    if closeResult != 0 {
        exit(1)
    }
    
    exit(runResult)
}

// Entry point
func main()
{
    var type = cpu_type_t()
    var size = MemoryLayout.size(ofValue: type)
    let isArmArchitecture = sysctlbyname("hw.cputype", &type, &size, nil, 0) == 0 && (type & 0xFF) == CPU_TYPE_ARM
    
    var useMono = false

    if !isArmArchitecture {
        if #available(macOS 10.15, *) {
            useMono = ProcessInfo.processInfo.environment["OPENRA_PREFER_MONO"] != nil
        } else {
            useMono = true
        }
    }
    
    if useMono {
        let task = Process()
        task.launchPath = Bundle.main.bundlePath.appending("/Contents/MacOS/checkmono")
        task.launch()
        task.waitUntilExit()

        if task.terminationStatus != 0 {
            NSLog("Utility requires Mono \(SYSTEM_MONO_MIN_VERSION) or later. Please install Mono and try again.\n")
            exit(1)
        }
    }
    
    guard let plist = Bundle.main.infoDictionary,
          let modId = plist["ModId"] as? String,
          !modId.isEmpty else {
        NSLog("Could not detect ModId\n")
        exit(1)
    }

    setenv("ENGINE_DIR", Bundle.main.bundlePath.appending("/Contents/Resources/"), 1)
    
    let args = CommandLine.arguments
    if useMono {
        launch_mono(args: args, modId: modId)
    } else {
        launch_dotnet(args: args, modId: modId, isArmArchitecture: isArmArchitecture)
    }
}

// Start the CLI program
main()
