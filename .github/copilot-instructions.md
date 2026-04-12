# PodScrub — Repo Context

## FFmpeg Base Image Dependency
- The Docker image `ghcr.io/mu88/podscrub-ffmpeg` must exist on GHCR before System Tests or `dotnet publish` (container) can run. It is built by `FFmpeg.yml`, which triggers only on pushes to `main` (when `Dockerfile-FFmpeg` or `FFmpeg.yml` change) or on `renovate/ffmpeg`- and `renovate/dotnet`-branches — not on every push. If System Tests fail with a missing image error, trigger `FFmpeg.yml` manually first.

## Network Isolation — AllowList is Mandatory
- All outgoing HTTP calls **must** use the named `"podcast"` HttpClient. The handler (`AllowListHttpMessageHandler`) blocks requests to any host not listed in `PodScrubOptions.Feeds[*].Url` or `Feeds[*].Jingles[*].SourceEpisodeUrl`. Adding a new HTTP call to an unlisted host will fail silently at runtime — always add the URL to the config first.

## Container: Non-Root Data Path
- The chiseled container runs as non-root `app` user — `/app/data` is not writable at startup. System tests and `docker-compose.yml` must use `PodScrub__DataPath=/tmp/data` (or a volume with correct permissions). Never hardcode `/app/data` as default in tests.

## CancellationToken in SoundFingerprintEngine
- `CancellationToken` is accepted by `IFingerprintEngine` methods but intentionally **not forwarded** to the `SoundFingerprinting` builder API — the library does not expose cancellation. This is a known library limitation, not a bug.

## Jingle Groups
- `InterludeStart` and `InterludeEnd` jingles must share the same `Group` value to form a pair. A jingle without a matching counterpart in the same group will not detect interludes. Always configure jingles in pairs.

## Episode State Persistence
- Processed episode state (including `[SCRUBBED]` suffix) is persisted as sidecar JSON files (`{id}.json`) in `DataPath` and reloaded on startup. Do not assume in-memory state is the only source of truth.
