import AppKit
import WebKit
import CoreLocation
import ServiceManagement
import Network
import UserNotifications
import Combine

/// Nimbus: a menu bar weather app. A small Swift shell (menu bar item, popover, location, open at login)
/// around the web screens in Resources/web, which fetch live weather from Open-Meteo.
@main
enum NimbusApp {
    @MainActor
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}

private let arrowH: CGFloat = 12
/// The web page is 380 wide and 620 tall plus a 12 pt strip at the top where the arrow sits; the sky gradient runs through that strip.
private let panelSize = NSSize(width: 380, height: 620 + arrowH)

/// A borderless glass panel that can take keys (for typing a city) without activating the app.
final class GlassPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

/// Draws only the thin glass rim; never takes clicks.
final class RimView: NSView {
    let rim = CAShapeLayer()
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        rim.fillColor = nil
        rim.lineWidth = 1
        layer?.addSublayer(rim)
    }
    required init?(coder: NSCoder) { fatalError() }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, WKScriptMessageHandler, WKNavigationDelegate, CLLocationManagerDelegate, UNUserNotificationCenterDelegate {
    private var statusItem: NSStatusItem!
    private let updater = AppUpdater(AppDelegate.updateConfig)
    private var updSub: AnyCancellable?
    static let updateConfig = UpdateConfig(repo: "Solomon-69/Nimbus", appName: "Nimbus", bundleName: "Nimbus.app", zipName: "Nimbus-mac.zip")
    private var panel: GlassPanel!
    private var container: NSView!
    private var rimView: RimView!
    private var shown = false
    private var arrowX: CGFloat = 190
    private var webView: WKWebView!
    private var clickMonitor: Any?
    private var keyMonitor: Any?
    private var styled = false
    private var quitting = false
    private var pageReady = false
    private var pendingLocate = false
    private var barKind = "cloud"
    private var barText = ""
    private var barStyle = "both"
    private var barTip = "Nimbus"

    private lazy var locationManager: CLLocationManager = {
        let m = CLLocationManager()
        m.delegate = self
        m.desiredAccuracy = kCLLocationAccuracyKilometer
        return m
    }()
    private let pathMonitor = NWPathMonitor()
    private var wasOnline = true

    private static let supportDir: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let dir = base.appendingPathComponent("Nimbus", isDirectory: true)
        let fm = FileManager.default
        if !fm.fileExists(atPath: dir.path) {
            // First launch after the rename from Weather: keep the saved places and settings.
            let old = base.appendingPathComponent("Weather", isDirectory: true)
            if fm.fileExists(atPath: old.path) { try? fm.copyItem(at: old, to: dir) }
        }
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }()
    private static var stateURL: URL { supportDir.appendingPathComponent("state.json") }

    // MARK: - Launch

    func applicationDidFinishLaunching(_ notification: Notification) {
        #if UPDATER_TEST
        if let arg = CommandLine.arguments.first(where: { $0.hasPrefix("--selftest-update=") }) {
            let path = String(arg.dropFirst("--selftest-update=".count))
            Task { @MainActor in exit(await AppUpdater.selfTest(Self.updateConfig, result: path) ? 0 : 1) }
            return
        }
        #endif
        // One copy only: a second launch just opens the panel of the first.
        let me = Bundle.main.bundleIdentifier ?? "com.solomon.Nimbus"
        if NSRunningApplication.runningApplications(withBundleIdentifier: me).count > 1 {
            DistributedNotificationCenter.default().postNotificationName(.init("com.solomon.Nimbus.open"), object: nil, userInfo: nil, deliverImmediately: true)
            NSApp.terminate(nil)
            return
        }
        DistributedNotificationCenter.default().addObserver(forName: .init("com.solomon.Nimbus.open"), object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.openPopover() }
        }
        if SMAppService.mainApp.status == .enabled { try? SMAppService.mainApp.register() }

        UNUserNotificationCenter.current().delegate = self
        updSub = updater.$state.sink { [weak self] s in self?.pushUpdate(s) }
        makeWebView()
        makeStatusItem()
        makePanel()

        Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.js("wx.tick()") }
        }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            // The network needs a moment after wake before a request can succeed.
            DispatchQueue.main.asyncAfter(deadline: .now() + 3) { MainActor.assumeIsolated { self?.js("wx.wake()") } }
        }
        pathMonitor.pathUpdateHandler = { [weak self] path in
            let online = path.status == .satisfied
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    guard let self else { return }
                    if online && !self.wasOnline { self.js("wx.online()") }
                    self.wasOnline = online
                }
            }
        }
        pathMonitor.start(queue: DispatchQueue(label: "nimbus.net"))
        #if NIMBUS_TEST
        DistributedNotificationCenter.default().addObserver(self, selector: #selector(testCommand(_:)), name: .init("com.solomon.Nimbus.test"), object: nil)
        #endif
        log("launched")
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        openPopover()
        return false
    }

    // MARK: - Web view

    private func makeWebView() {
        let stored = (try? String(contentsOf: Self.stateURL, encoding: .utf8)) ?? ""
        let literal = (try? JSONSerialization.data(withJSONObject: [stored])).flatMap { String(data: $0, encoding: .utf8) } ?? "[\"\"]"
        let config = WKWebViewConfiguration()
        let content = WKUserContentController()
        content.addUserScript(WKUserScript(source: "window.__WX_STORE__ = (\(literal))[0] || null;", injectionTime: .atDocumentStart, forMainFrameOnly: true))
        content.add(self, name: "wx")
        config.userContentController = content
        // The pages are our own bundled files; they call Open-Meteo directly.
        config.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")
        config.setValue(true, forKey: "allowUniversalAccessFromFileURLs")
        webView = WKWebView(frame: NSRect(origin: .zero, size: panelSize), configuration: config)
        webView.setValue(false, forKey: "drawsBackground")
        webView.navigationDelegate = self
        webView.setAccessibilityLabel("Nimbus")
        if let dir = Bundle.main.resourceURL?.appendingPathComponent("web", isDirectory: true) {
            webView.loadFileURL(dir.appendingPathComponent("index.html"), allowingReadAccessTo: dir)
        }
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        decisionHandler(navigationAction.request.url?.isFileURL == true ? .allow : .cancel)
    }

    private func js(_ code: String) {
        webView?.evaluateJavaScript(code, completionHandler: nil)
    }

    nonisolated func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any], let action = body["a"] as? String else { return }
        DispatchQueue.main.async {
            MainActor.assumeIsolated { self.handle(action, body) }
        }
    }

    private func handle(_ action: String, _ body: [String: Any]) {
        switch action {
        case "ready":
            pageReady = true
            hostInfo()
        case "save":
            if let json = body["json"] as? String { try? json.write(to: Self.stateURL, atomically: true, encoding: .utf8) }
        case "bar":
            barKind = body["kind"] as? String ?? "cloud"
            barText = body["text"] as? String ?? ""
            barStyle = body["style"] as? String ?? "both"
            barTip = body["tip"] as? String ?? "Nimbus"
            let theme = body["theme"] as? String ?? "auto"
            panel.appearance = theme == "light" ? NSAppearance(named: .aqua) : theme == "dark" ? NSAppearance(named: .darkAqua) : nil
            updateRim()
            if body["empty"] as? Bool == true { barText = "" }
            updateStatusItem()
        case "locate":
            requestLocation()
        case "login":
            setLogin(body["on"] as? Bool ?? false)
        case "notifyPermission":
            askNotifyPermission()
        case "notify":
            sendNotice(title: body["title"] as? String ?? "Nimbus", body: body["body"] as? String ?? "")
        case "updCheck":
            Task { await updater.check() }
        case "updInstall":
            Task { await updater.install() }
        case "quit":
            quitAnimated()
        default: break
        }
    }

    private func pushUpdate(_ s: AppUpdater.State) {
        var o: [String: Any] = ["current": updater.currentVersion]
        switch s {
        case .idle: o["state"] = "idle"
        case .checking: o["state"] = "checking"
        case .upToDate: o["state"] = "uptodate"
        case .notPublished: o["state"] = "none"
        case .available(let v): o["state"] = "available"; o["version"] = v
        case .downloading(let p): o["state"] = "downloading"; o["pct"] = Int(p)
        case .installing: o["state"] = "installing"
        case .failed(let m): o["state"] = "failed"; o["msg"] = m
        }
        if let d = try? JSONSerialization.data(withJSONObject: o), let j = String(data: d, encoding: .utf8) { js("wx.update(\(j))") }
    }

    private func hostInfo() {
        // Units follow the Mac's region until the user picks their own.
        let system = Locale.current.measurementSystem
        let metric = system != .us
        let mph = system == .us || system == .uk
        UNUserNotificationCenter.current().getNotificationSettings { settings in
            let ok = settings.authorizationStatus == .authorized || settings.authorizationStatus == .provisional
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.js("wx.hostInfo({platform:'mac',canLocate:true,canNotify:\(ok),metric:\(metric),mph:\(mph),login:\(SMAppService.mainApp.status == .enabled)})")
                }
            }
        }
    }

    // MARK: - Rain alerts

    private func askNotifyPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in
            DispatchQueue.main.async { MainActor.assumeIsolated { self.hostInfo(); self.js("wx.state && WxEngine.checkRain()") } }
        }
    }

    private func sendNotice(title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
        log("notice: \(title) / \(body)")
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }

    // MARK: - Menu bar item

    private func makeStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.autosaveName = "NimbusItem"
        if let button = statusItem.button {
            button.wantsLayer = true
            button.target = self
            button.action = #selector(statusClicked)
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
            button.imagePosition = .imageLeading
            button.setAccessibilityLabel("Nimbus")
        }
        updateStatusItem()
    }

    private static let symbols: [String: String] = [
        "clear": "sun.max.fill", "clear-n": "moon.stars.fill", "partly": "cloud.sun.fill", "partly-n": "cloud.moon.fill",
        "cloud": "cloud.fill", "rain": "cloud.rain.fill", "drizzle": "cloud.drizzle.fill", "snow": "cloud.snow.fill",
        "thunder": "cloud.bolt.fill", "fog": "cloud.fog.fill", "wind": "wind",
    ]

    private var lastShownKey = ""
    private func updateStatusItem() {
        guard let button = statusItem.button else { return }
        let showIcon = barStyle != "temp" || barText.isEmpty
        let showText = barStyle != "icon" && !barText.isEmpty
        // Only swap the picture when it really changes (swapping it constantly leaves ghost copies on macOS 26).
        let key = (showIcon ? barKind : "-") + "|" + (showText ? barText : "-")
        guard key != lastShownKey else { return }
        lastShownKey = key
        if showIcon {
            let config = NSImage.SymbolConfiguration(pointSize: 13, weight: .regular)
            let image = NSImage(systemSymbolName: Self.symbols[barKind] ?? "cloud.fill", accessibilityDescription: barTip)?.withSymbolConfiguration(config)
            image?.isTemplate = true
            button.image = image
        } else {
            button.image = nil
        }
        button.title = showText ? (showIcon ? " " : "") + barText : ""
        button.toolTip = barTip
    }

    // MARK: - Glass panel

    private func makePanel() {
        panel = GlassPanel(contentRect: NSRect(origin: .zero, size: panelSize), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .popUpMenu
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        let root = NSView(frame: NSRect(origin: .zero, size: panelSize))
        root.wantsLayer = true
        panel.contentView = root
        container = NSView(frame: root.bounds)
        container.wantsLayer = true
        root.addSubview(container)
        let glass = NSVisualEffectView(frame: container.bounds)
        glass.material = .popover
        glass.blendingMode = .behindWindow
        glass.state = .active
        glass.autoresizingMask = [.width, .height]
        container.addSubview(glass)
        webView.frame = container.bounds
        webView.autoresizingMask = [.width, .height]
        container.addSubview(webView)
        rimView = RimView(frame: root.bounds)
        root.addSubview(rimView)
        setShape(arrowX: arrowX)
    }

    /// Rounded body with the arrow cut into the top edge, so the sky gradient flows right up into the arrow.
    private static func shape(_ size: NSSize, arrowX ax: CGFloat) -> CGPath {
        let r: CGFloat = 22, top = size.height - arrowH, p = CGMutablePath()
        p.move(to: CGPoint(x: r, y: 0))
        p.addLine(to: CGPoint(x: size.width - r, y: 0))
        p.addArc(tangent1End: CGPoint(x: size.width, y: 0), tangent2End: CGPoint(x: size.width, y: r), radius: r)
        p.addLine(to: CGPoint(x: size.width, y: top - r))
        p.addArc(tangent1End: CGPoint(x: size.width, y: top), tangent2End: CGPoint(x: size.width - r, y: top), radius: r)
        p.addLine(to: CGPoint(x: ax + 15, y: top))
        p.addCurve(to: CGPoint(x: ax + 3, y: top + arrowH - 1.6), control1: CGPoint(x: ax + 8, y: top), control2: CGPoint(x: ax + 6, y: top + arrowH - 2))
        p.addQuadCurve(to: CGPoint(x: ax - 3, y: top + arrowH - 1.6), control: CGPoint(x: ax, y: top + arrowH + 1.2))
        p.addCurve(to: CGPoint(x: ax - 15, y: top), control1: CGPoint(x: ax - 6, y: top + arrowH - 2), control2: CGPoint(x: ax - 8, y: top))
        p.addLine(to: CGPoint(x: r, y: top))
        p.addArc(tangent1End: CGPoint(x: 0, y: top), tangent2End: CGPoint(x: 0, y: top - r), radius: r)
        p.addLine(to: CGPoint(x: 0, y: r))
        p.addArc(tangent1End: CGPoint(x: 0, y: 0), tangent2End: CGPoint(x: r, y: 0), radius: r)
        p.closeSubpath()
        return p
    }

    private func setShape(arrowX ax: CGFloat) {
        let path = Self.shape(panelSize, arrowX: ax)
        let mask = CAShapeLayer()
        mask.path = path
        container.layer?.mask = mask
        rimView.rim.path = path
        updateRim()
    }

    /// Springs the panel out of (or back into) the arrow tip, like the other menu bar apps.
    private func springPanel(opening: Bool) {
        guard !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        let w = panelSize.width, h = panelSize.height
        let s0: CGFloat = opening ? 0.97 : 0.97
        var small = CATransform3DMakeTranslation((arrowX - w / 2) * (1 - s0), (h / 2) * (1 - s0), 0)
        small = CATransform3DScale(small, s0, s0, 1)
        let spring = CASpringAnimation(keyPath: "transform")
        if opening {
            spring.fromValue = small
            spring.toValue = CATransform3DIdentity
            spring.mass = 1; spring.stiffness = 520; spring.damping = 38
            spring.duration = spring.settlingDuration
        } else {
            spring.fromValue = CATransform3DIdentity
            spring.toValue = small
            spring.duration = 0.14
            spring.timingFunction = CAMediaTimingFunction(name: .easeIn)
            spring.isRemovedOnCompletion = true
        }
        for layer in [container.layer, rimView.layer] { layer?.add(spring, forKey: "spring") }
    }

    private func updateRim() {
        let dark = (panel.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua)
        rimView?.rim.strokeColor = NSColor.white.withAlphaComponent(dark ? 0.16 : 0.5).cgColor
    }

    private func openPopover() {
        guard let button = statusItem.button, let bw = button.window, !shown else { return }
        let icon = bw.frame, screen = bw.screen ?? NSScreen.main ?? NSScreen.screens[0]
        let midX = icon.midX
        let x = min(max(midX - panelSize.width / 2, screen.frame.minX + 8), screen.frame.maxX - panelSize.width - 8)
        arrowX = midX - x
        setShape(arrowX: arrowX)
        let finalFrame = NSRect(x: x, y: icon.minY - panelSize.height + 1, width: panelSize.width, height: panelSize.height)
        shown = true
        bounceIcon()
        button.highlight(true)
        panel.alphaValue = 0
        panel.setFrame(finalFrame, display: true)
        panel.orderFrontRegardless()
        panel.makeKey()
        panel.makeFirstResponder(webView)
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.1
            ctx.timingFunction = CAMediaTimingFunction(name: .easeOut)
            panel.animator().alphaValue = 1
        }
        springPanel(opening: true)
        panel.invalidateShadow()
        js("wx.opened()")   // the page hides its stale picture itself and fades the fresh one in (see engine.js)
        clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            DispatchQueue.main.async { MainActor.assumeIsolated { self?.closePopover() } }
        }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard event.keyCode == 53, let self else { return event }   // Esc
            self.webView.evaluateJavaScript("WxUI.escape()") { result, _ in
                if (result as? Bool) != true { self.closePopover() }
            }
            return nil
        }
    }

    private func closePopover() {
        guard shown else { return }
        shown = false
        statusItem.button?.highlight(false)
        if let m = clickMonitor { NSEvent.removeMonitor(m); clickMonitor = nil }
        if let m = keyMonitor { NSEvent.removeMonitor(m); keyMonitor = nil }
        js("wx.closed()")
        springPanel(opening: false)
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.14
            panel.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            MainActor.assumeIsolated {
                guard let self, !self.shown else { return }
                self.panel.orderOut(nil)
                if self.quitting { NSApp.terminate(nil) }
            }
        })
    }

    @objc private func statusClicked() {
        if shown { closePopover() } else { openPopover() }
    }

    private func quitAnimated() {
        quitting = true
        if shown { closePopover() } else { NSApp.terminate(nil) }
    }

    private func bounceIcon() {
        guard !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion, let button = statusItem.button, let layer = button.layer else { return }
        let b = button.bounds
        var from = CATransform3DMakeTranslation(b.midX, b.midY, 0)
        from = CATransform3DScale(from, 1.12, 1.12, 1)
        from = CATransform3DTranslate(from, -b.midX, -b.midY, 0)
        let bounce = CASpringAnimation(keyPath: "transform")
        bounce.fromValue = from
        bounce.toValue = CATransform3DIdentity
        bounce.mass = 1; bounce.stiffness = 320; bounce.damping = 11
        bounce.duration = bounce.settlingDuration
        layer.add(bounce, forKey: "bounce")
    }

    // MARK: - Open at login

    private func setLogin(_ on: Bool) {
        do { if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() } }
        catch { log("login change failed: \(error.localizedDescription)") }
        hostInfo()
    }

    // MARK: - Location

    private func requestLocation() {
        pendingLocate = true
        switch locationManager.authorizationStatus {
        case .notDetermined: locationManager.requestWhenInUseAuthorization()
        case .denied, .restricted: pendingLocate = false; sendLocation(["ok": false, "reason": "denied"])
        default: locationManager.requestLocation()
        }
    }

    private func sendLocation(_ dict: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: dict), let text = String(data: data, encoding: .utf8) else { return }
        js("wx.location(\(text))")
    }

    nonisolated func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let status = manager.authorizationStatus
        DispatchQueue.main.async {
            MainActor.assumeIsolated {
                guard self.pendingLocate else { return }
                switch status {
                case .denied, .restricted: self.pendingLocate = false; self.sendLocation(["ok": false, "reason": "denied"])
                case .notDetermined: break
                default: manager.requestLocation()
                }
            }
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last else { return }
        CLGeocoder().reverseGeocodeLocation(loc) { marks, _ in
            let mark = marks?.first
            let name = mark?.locality ?? mark?.name ?? "Current location"
            let sub = [mark?.administrativeArea, mark?.country].compactMap { $0 }.joined(separator: ", ")
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.pendingLocate = false
                    self.sendLocation(["ok": true, "lat": loc.coordinate.latitude, "lon": loc.coordinate.longitude, "name": name, "sub": sub])
                }
            }
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        let denied = (error as? CLError)?.code == .denied
        DispatchQueue.main.async {
            MainActor.assumeIsolated {
                self.pendingLocate = false
                self.sendLocation(["ok": false, "reason": denied ? "denied" : "failed"])
            }
        }
    }

    // MARK: - Log and test hook

    private func log(_ line: String) {
        let url = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/Nimbus.log")
        let text = "\(ISO8601DateFormatter().string(from: Date())) \(line)\n"
        if let handle = try? FileHandle(forWritingTo: url) { handle.seekToEndOfFile(); handle.write(Data(text.utf8)); try? handle.close() }
        else { try? text.write(to: url, atomically: true, encoding: .utf8) }
    }

    #if NIMBUS_TEST
    /// UI-only hook for scripts: open or close the panel, run a bit of JS and log the answer.
    @objc private func testCommand(_ note: Notification) {
        guard let command = note.object as? String else { return }
        switch command {
        case "panel": openPopover()
        case "close": closePopover()
        case "toggle": statusClicked()
        case "sample":
            // Opens the panel and logs the panel's scale and opacity every 25 ms, to prove the spring-in.
            closePopover()
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                MainActor.assumeIsolated {
                    self.openPopover()
                    var out: [String] = []
                    for i in 0..<24 {
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.025 * Double(i)) {
                            MainActor.assumeIsolated {
                                let t = self.container.layer?.presentation()?.transform.m11 ?? -1
                                out.append(String(format: "%.3f/%.2f", t, self.panel.alphaValue))
                                if i == 23 { self.log("spring m11/alpha: " + out.joined(separator: " ")) }
                            }
                        }
                    }
                }
            }
        case "wake": js("wx.wake()")
        case "tick": js("wx.tick()")
        case "bar": log("bar kind=\(barKind) text=\(barText) style=\(barStyle) title=\(statusItem.button?.title ?? "") hasImage=\(statusItem.button?.image != nil)")
        case "frame":
            if let w = statusItem.button?.window { log("icon frame=\(NSStringFromRect(w.frame)) popover=\(shown) pop=\(NSStringFromRect(panel.frame))") }
        default:
            if command.hasPrefix("js:") {
                let code = String(command.dropFirst(3))
                webView.evaluateJavaScript(code) { [weak self] result, error in
                    MainActor.assumeIsolated { self?.log("js \(code) -> \(String(describing: result ?? error?.localizedDescription ?? "nil"))") }
                }
            }
        }
    }
    #endif
}
