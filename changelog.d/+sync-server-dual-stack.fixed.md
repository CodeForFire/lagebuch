The sync server only bound IPv4 (`0.0.0.0`), so a device reachable only over IPv6 could never
join a hosted incident. It now binds dual-stack, accepting both IPv4 and IPv6 on the same
socket, and falls back to IPv4-only itself if the platform doesn't support IPv6.
