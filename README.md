# L2 Tracker Companion

Windows desktop companion for the [Lineage 2 Farm Tracker](https://l2tracker.cc). Watches a running L2 client, reads the in-game Play Report panel via OCR, and submits farm sessions through the existing backend API.

This repository is the desktop app only. The web UI and API live in [`l2-tracker-frontend`](https://github.com/gregpopiel/l2-tracker-frontend) and [`l2-tracker-backend`](https://github.com/gregpopiel/l2-tracker-backend) and are deployed independently.

## Stack

| Piece | Choice |
| :--- | :--- |
| Runtime | .NET 8 (`net8.0-windows`) |
| UI | WPF |
| OCR | `Windows.Media.Ocr` (built into Windows) |
| Local storage | SQLite (planned — not yet in this skeleton) |

## Requirements

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for development; the published app is self-contained)

## Local setup

```bash
dotnet build
dotnet run --project L2TrackerCompanion
```

A single empty window titled **L2 Tracker Companion** should open.

## Publish (later)

```bash
dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Implementation plan

Step-by-step work is tracked in the parent workspace [`PLAN.md`](../PLAN.md) (sibling folder, not part of this repo).
