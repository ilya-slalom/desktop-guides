# Desktop Guides

Desktop Guides is a Windows app for keeping written game guides together
and reading them offline. You add your games to a library, import the
guides you already have or save them from a few supported sites, and read
them in one place. Each guide remembers where you were, and you mark a
guide complete yourself when you've finished it. The app is built with
WinUI 3 and .NET 10.

## What it does

- **A library of games,** with details and cover art from IGDB and
  SteamGridDB using your own keys, or added by hand.
- **Imports TXT, HTML and PDF guides,** guides saved as several web pages,
  and PNG or JPEG image maps. Each is copied into the library, and the
  original file is left untouched.
- **Find online** saves guides from GameFAQs, Neoseeker, Steam Community
  and The Video Game Atlas (VGMaps) for offline reading, credited to their
  authors.
- **A reader for each format,** with a contents pane, search in the guide,
  bookmarks, text size or zoom, and light and dark themes. Windows high
  contrast takes precedence.
- **Your place in every guide** is kept between sessions, and completion
  is a separate choice that reading never changes.
- **Works offline.** Everything in the library opens with no network.
- **Backup and restore** of the whole library to a `.zip` file.
- **Keyboard shortcuts** for the library, the reader and Find online.

## Supported Windows

| Target | Status |
| --- | --- |
| Windows 11 x64 | Tested for each release |
| Windows 11 ARM64 | Not released |
| Windows 10 | Not claimed yet |

The [tested matrix](docs/release/tested-matrix.md) records what each
release was tested on.

## Install

Download the latest release from the
[GitHub Releases page](https://github.com/ilya-slalom/desktop-guides/releases).
Each release has two builds:

- **The MSIX package** installs the app. It's signed with the project's
  own certificate, which you trust once before the first install. Later
  releases upgrade it in place and keep your library.
- **The portable build** is one `.exe` in a `.zip` that runs without
  installing.

The [user guide](docs/user-guide.md#installing) has the steps, including
the prerequisites and how to check the certificate.

## Documentation

For people using the app:
- [User guide](docs/user-guide.md): installing, the library, importing,
  Find online, reading, backup, keyboard shortcuts and troubleshooting.

For contributors:
- [Architecture](docs/architecture.md): how the app is put together.
- [Contributing](docs/contributing.md): building the app and making a
  change.
- [Testing](docs/testing.md): the test suites and how to run them.
- [Releasing](docs/releasing.md): how a release is built, signed and
  published.
- [Sources](docs/sources.md): the online sources and the terms each one
  is used under.
- [Roadmap](docs/roadmap.md): what's planned next.
- [Tested matrix](docs/release/tested-matrix.md) and
  [release notes template](docs/release/release-notes-template.md).

For coding agents:
- [AGENTS.md](AGENTS.md): project guidance for agents working in this
  repository.

## Documentation history

The design documents, plans and results written while the app was built
were removed on 25 October 2026. They're kept under the git tag
[`docs-history-2026-10-25`](https://github.com/ilya-slalom/desktop-guides/tree/docs-history-2026-10-25).
