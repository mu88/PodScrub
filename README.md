# PodScrub

![Combined CI / Release](https://github.com/mu88/PodScrub/actions/workflows/CI_CD.yml/badge.svg)
![Mutation testing](https://github.com/mu88/PodScrub/actions/workflows/Mutation%20Testing.yml/badge.svg)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=mu88_PodScrub&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=mu88_PodScrub)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=mu88_PodScrub&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=mu88_PodScrub)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=mu88_PodScrub&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=mu88_PodScrub)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=mu88_PodScrub&metric=bugs)](https://sonarcloud.io/summary/new_code?id=mu88_PodScrub)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=mu88_PodScrub&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=mu88_PodScrub)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=mu88_PodScrub&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=mu88_PodScrub)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=mu88_PodScrub&metric=coverage)](https://sonarcloud.io/summary/new_code?id=mu88_PodScrub)
[![Mutation testing badge](https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2Fmu88%2FPodScrub%2Fmain)](https://dashboard.stryker-mutator.io/reports/github.com/mu88/PodScrub/main)

A self-hosted podcast feed proxy that automatically detects and removes certain audio parts using audio fingerprinting — just point your podcast client to the clean feed URL.

## How it works

PodScrub acts as a proxy between your podcast client and the original podcast feeds:

1. **Configure** feeds and audio fingerprints (jingles) in a YAML file
2. **PodScrub polls** the original feeds periodically for new episodes
3. **Audio fingerprinting** detects the configured jingles in each episode
4. **Matching segments** between jingle pairs are seamlessly removed via ffmpeg
5. **A clean RSS feed** is served under a new URL — point your podcast client there

## Network isolation

PodScrub is designed to have **very limited internet access**. An internal allow-list only permits outbound HTTP requests to the hosts of configured feed URLs. All other outbound requests are blocked.

## Configuration

Feeds are configured via the standard .NET Options Pattern — either in `appsettings.json` or via environment variables (recommended for Docker).

### appsettings.json example

```json
{
  "PodScrub": {
    "BaseUrl": "http://my-server:8080",
    "PollIntervalMinutes": 60,
    "MaxEpisodesPerFeed": 50,
    "TransitionTonePath": "/app/data/transition.mp3",
    "Feeds": [
      {
        "Name": "my-podcast",
        "Url": "https://example.com/feed.rss",
        "ProcessLatest": 20,
        "Jingles": [
          {
            "Type": "InterludeStart",
            "Group": "main-interlude",
            "SourceEpisode": "https://example.com/episode123.mp3",
            "TimestampStart": "00:12:34",
            "TimestampEnd": "00:12:38"
          },
          {
            "Type": "InterludeEnd",
            "Group": "main-interlude",
            "SourceEpisode": "https://example.com/episode123.mp3",
            "TimestampStart": "00:15:00",
            "TimestampEnd": "00:15:04"
          }
        ]
      }
    ]
  }
}
```

### Environment variable example (Docker)

```bash
PodScrub__TransitionTonePath=/app/data/transition.mp3
PodScrub__Feeds__0__Name=my-podcast
PodScrub__Feeds__0__Url=https://example.com/feed.rss
PodScrub__Feeds__0__ProcessLatest=20
PodScrub__Feeds__0__Jingles__0__Type=InterludeStart
PodScrub__Feeds__0__Jingles__0__Group=main-interlude
PodScrub__Feeds__0__Jingles__0__SourceEpisode=https://example.com/episode123.mp3
PodScrub__Feeds__0__Jingles__0__TimestampStart=00:12:34
PodScrub__Feeds__0__Jingles__0__TimestampEnd=00:12:38
PodScrub__Feeds__0__Jingles__1__Type=InterludeEnd
PodScrub__Feeds__0__Jingles__1__Group=main-interlude
PodScrub__Feeds__0__Jingles__1__SourceEpisode=https://example.com/episode123.mp3
PodScrub__Feeds__0__Jingles__1__TimestampStart=00:15:00
PodScrub__Feeds__0__Jingles__1__TimestampEnd=00:15:04
```

### Global options

| Option | Description |
|---|---|
| `BaseUrl` | Public base URL for the PodScrub instance |
| `PollIntervalMinutes` | How often to poll feeds (default: 60) |
| `MaxEpisodesPerFeed` | Maximum episodes to track per feed (default: 50) |
| `DataPath` | Directory for data storage (default: `./data`) |
| `TransitionTonePath` | *(optional)* Path to an audio file (e.g. MP3, WAV) inserted between content segments where interludes were removed. If omitted, segments are joined without any audible marker. |

### Feed options

| Option | Description |
|---|---|
| `Name` | Unique feed identifier (used in the clean feed URL) |
| `Url` | Original podcast feed URL |
| `ProcessLatest` | *(optional)* Only process the N most recent episodes (by publish date). Older episodes are skipped entirely — useful for long-running podcasts where old episodes have already been listened to. |
| `Jingles` | List of audio fingerprints to detect |

### Jingle options

| Option | Description |
|---|---|
| `Type` | `InterludeStart` (before the segment) or `InterludeEnd` (after the segment) |
| `Group` | *(optional)* Groups jingles into pairs. Matching is performed within each group, so a start jingle from one group will never be paired with an end jingle from another. Defaults to `default` when omitted. |
| `SourceEpisode` | URL of a podcast episode containing this jingle |
| `TimestampStart` | Where the jingle starts in the source episode (e.g. `00:12:34`) |
| `TimestampEnd` | Where the jingle ends in the source episode (e.g. `00:12:38`) |

PodScrub pairs `interlude_start` and `interlude_end` matches chronologically within each group: the first `interlude_start` match is paired with the next `interlude_end` match after it.

Some podcasts change their jingles over time or use different jingles for different types of breaks. Add multiple groups to detect all of them — each group is matched independently.

### How to find jingle timestamps

1. Listen to a podcast episode and note where the jingle before the segment starts and ends (e.g. `00:12:34` to `00:12:38`)
2. Do the same for the jingle after the segment (`00:15:00` to `00:15:04`)
3. Add both to your configuration as shown above — if the podcast uses different jingles, add a separate `Group` for each pair
4. PodScrub will extract those clips and use them as fingerprints for all future episodes

## Deployment

### Docker Compose (with egress proxy)

PodScrub is designed to run with **minimal internet access**. The recommended setup uses an internal Docker network with an egress proxy that only allows traffic to configured podcast hosts:

```yaml
services:
  podscrub:
    image: ghcr.io/mu88/podscrub-api:latest
    ports:
      - "8080:8080"
    networks:
      - podscrub-internal
    volumes:
      - podscrub-data:/app/data
    environment:
      - PodScrub__BaseUrl=http://my-server:8080
      - PodScrub__PollIntervalMinutes=60
      - PodScrub__MaxEpisodesPerFeed=50
      - PodScrub__Feeds__0__Name=my-podcast
      - PodScrub__Feeds__0__Url=https://example.com/feed.rss
      # ... (see docker-compose.yml for full example)
      - http_proxy=http://egress-proxy:8888
      - https_proxy=http://egress-proxy:8888
    depends_on:
      - egress-proxy

  egress-proxy:
    image: monokal/tinyproxy:latest
    networks:
      - podscrub-internal
      - egress
    volumes:
      - ./tinyproxy/filter:/etc/tinyproxy/filter:ro
    environment:
      - ALLOWED=0.0.0.0/0
      - FILTERED=yes

networks:
  podscrub-internal:
    internal: true   # no direct internet access for PodScrub
  egress:            # egress proxy can reach the internet

volumes:
  podscrub-data:
```

Create `tinyproxy/filter` with one allowed hostname per line (the podcast feed hosts from your configuration):

```
feeds.example.com
other.example.com
```

### Application settings

| Setting | Default | Description |
|---|---|---|
| `PodScrub__BaseUrl` | `http://localhost:8080` | Public base URL (used in generated feed XML) |
| `PodScrub__PollIntervalMinutes` | `60` | How often to poll feeds for new episodes |
| `PodScrub__MaxEpisodesPerFeed` | `50` | Number of episodes to keep per feed (oldest are evicted) |
| `PodScrub__DataPath` | `./data` | Directory for processed audio files |
| `PodScrub__Feeds__N__*` | *(none)* | Feed configuration (see Configuration section above) |

### Storage management

- **Original downloads** are automatically deleted after successful processing
- **Processed audio** is retained up to `MaxEpisodesPerFeed` episodes per feed — oldest episodes beyond this limit are evicted and their files deleted

### Feed URL

Once running, your clean feed is available at:

```
http://<host>:8080/podscrub/feed/<feed-name>/rss.xml
```

For the example above: `http://my-server:8080/podscrub/feed/my-podcast/rss.xml`

Update your podcast client to use this URL instead of the original feed.

## Technology

- ASP.NET Core 10 Minimal API
- SoundFingerprinting (audio fingerprint matching)
- FFMpegCore (audio processing)
- CodeHollow.FeedReader (RSS parsing)
- YamlDotNet (configuration)
- Docker (regular + chiseled containers for linux-x64 and linux-arm64)

## Local development

```shell
dotnet run --project src/PodScrub.Api
```

The application starts at `http://localhost:5000/podscrub`.

## Supported platforms

The Docker image is published for `linux-x64` and `linux-arm64`, making it suitable for x86 servers and ARM devices like Raspberry Pi.
