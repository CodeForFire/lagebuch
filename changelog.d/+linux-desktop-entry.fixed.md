Linux: the desktop entry is complete and the app icon resolves in the menu and the dock. The
entry gained window-to-launcher matching (`StartupWMClass`), search keywords, a subtitle and an
accurate description in `apt show`. The `.deb` had shipped a single 1024px file filed under the
theme's `512x512` directory and depended on nothing, so on a system without a desktop icon
theme there was no theme to find it in at all, and everywhere else every size — panel, menu,
dock, app grid — was scaled down from that one megapixel image. It now installs a real file for
each hicolor size plus the scalable SVG, and depends on `hicolor-icon-theme`. (#310)
