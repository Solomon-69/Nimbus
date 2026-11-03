import Foundation
// Usage: wxtest <command>   sends a UI-only test command to the running Weather app
let cmd = CommandLine.arguments.dropFirst().joined(separator: " ")
DistributedNotificationCenter.default().postNotificationName(.init("com.solomon.Weather.test"), object: cmd, userInfo: nil, deliverImmediately: true)
RunLoop.current.run(until: Date().addingTimeInterval(0.3))
