import AppKit

// Draws the Weather app icon (soft blue sky tile, warm sun peeking over a white cloud) into an .iconset folder.
// Usage: mkicon <output-dir>   ->  <output-dir>/Weather.iconset

func draw(size: Int) -> NSImage {
    let s = CGFloat(size)
    return NSImage(size: NSSize(width: s, height: s), flipped: false) { rect in
        guard let ctx = NSGraphicsContext.current?.cgContext else { return false }
        let tile = rect.insetBy(dx: s * 0.06, dy: s * 0.06)
        let path = CGPath(roundedRect: tile, cornerWidth: s * 0.225, cornerHeight: s * 0.225, transform: nil)
        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: -s * 0.012), blur: s * 0.03, color: NSColor.black.withAlphaComponent(0.28).cgColor)
        ctx.addPath(path); ctx.setFillColor(NSColor(red: 0.42, green: 0.64, blue: 0.90, alpha: 1).cgColor); ctx.fillPath()
        ctx.restoreGState()
        ctx.saveGState(); ctx.addPath(path); ctx.clip()
        let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: [NSColor(red: 0.31, green: 0.52, blue: 0.86, alpha: 1).cgColor, NSColor(red: 0.62, green: 0.80, blue: 0.96, alpha: 1).cgColor, NSColor(red: 0.84, green: 0.92, blue: 0.98, alpha: 1).cgColor] as CFArray, locations: [0, 0.62, 1])!
        ctx.drawLinearGradient(grad, start: CGPoint(x: s / 2, y: tile.maxY), end: CGPoint(x: s / 2, y: tile.minY), options: [])
        // Sun
        let c = CGPoint(x: s * 0.60, y: s * 0.60), r = s * 0.15
        ctx.setStrokeColor(NSColor(red: 1.0, green: 0.80, blue: 0.36, alpha: 1).cgColor)
        ctx.setLineWidth(s * 0.032); ctx.setLineCap(.round)
        for i in 0..<8 {
            let a = CGFloat(i) * .pi / 4
            ctx.move(to: CGPoint(x: c.x + cos(a) * (r + s * 0.045), y: c.y + sin(a) * (r + s * 0.045)))
            ctx.addLine(to: CGPoint(x: c.x + cos(a) * (r + s * 0.095), y: c.y + sin(a) * (r + s * 0.095)))
        }
        ctx.strokePath()
        ctx.setFillColor(NSColor(red: 1.0, green: 0.80, blue: 0.36, alpha: 1).cgColor)
        ctx.fillEllipse(in: CGRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
        // Cloud
        ctx.setShadow(offset: CGSize(width: 0, height: -s * 0.01), blur: s * 0.03, color: NSColor.black.withAlphaComponent(0.18).cgColor)
        ctx.setFillColor(NSColor.white.cgColor)
        for (x, y, d) in [(0.30, 0.34, 0.17), (0.43, 0.40, 0.23), (0.59, 0.35, 0.19)] as [(CGFloat, CGFloat, CGFloat)] {
            ctx.fillEllipse(in: CGRect(x: s * x, y: s * y - s * d / 2, width: s * d, height: s * d))
        }
        let base = CGPath(roundedRect: CGRect(x: s * 0.29, y: s * 0.245, width: s * 0.50, height: s * 0.13), cornerWidth: s * 0.065, cornerHeight: s * 0.065, transform: nil)
        ctx.addPath(base); ctx.fillPath()
        ctx.restoreGState()
        return true
    }
}

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let set = (out as NSString).appendingPathComponent("Weather.iconset")
try? FileManager.default.createDirectory(atPath: set, withIntermediateDirectories: true)
let sizes: [(String, Int)] = [
    ("icon_16x16", 16), ("icon_16x16@2x", 32), ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256), ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024),
]
for (name, px) in sizes {
    let img = draw(size: px)
    guard let tiff = img.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff), let png = rep.representation(using: .png, properties: [:]) else { continue }
    try? png.write(to: URL(fileURLWithPath: (set as NSString).appendingPathComponent(name + ".png")))
}
