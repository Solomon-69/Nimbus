import Foundation
// Usage: wxtest <command>   sends a UI-only test command to the running Nimbus app
let cmd = CommandLine.arguments.dropFirst().joined(separator: " ")
DistributedNotificationCenter.default().postNotificationName(.init("com.solomon.Nimbus.test"), object: cmd, userInfo: nil, deliverImmediately: true)
RunLoop.current.run(until: Date().addingTimeInterval(0.3))
