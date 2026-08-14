# LocalWhale Public GitHub Release Design

## Context

LocalWhale is a Windows 11 x64 desktop shell for the official DeepSeek Harness Web UI. The working tree contains the WinUI 3 shell, lifecycle and update services, the Cordis desktop bridge, tests, Inno Setup packaging, and offline-runtime build tooling. The repository currently has no public remote, no root project license, and only a short English README.

The first public release must be useful to both end users who want an installer and developers who want to inspect or build the source. It must not commit bundled runtimes, generated installers, credentials, local Harness data, logs, or other machine-specific artifacts.

## Goals

- Create a public GitHub repository named `LocalWhale` with `main` as its default branch.
- Publish the project source under the MIT License.
- Preserve the separate DeepSeek Harness MIT license and third-party notices.
- Replace the short README with a polished bilingual project page: Chinese primary content plus an English summary.
- Publish a `v0.1.0` GitHub Release containing the offline x64 installer and its SHA-256 checksum.
- Add continuous integration for the .NET and desktop-bridge test suites.
- Fix and verify the known first-launch profile-module resolution race before producing the release installer.

## Non-goals

- Do not commit Node.js, pnpm, DeepSeek Harness, WebView2 redistributables, generated installers, logs, or `%LOCALAPPDATA%` data.
- Do not publish user credentials, `~/.dsh`, WebView2 profiles, runtime state, or local test data.
- Do not add automatic code signing, Microsoft Store distribution, Windows 10 support, ARM64 support, or a hosted update service in this release.
- Do not fork or modify the official Harness Web UI.

## Repository Presentation

The repository description will identify LocalWhale as a Fluent Windows desktop shell for DeepSeek Harness. Suggested topics are `deepseek`, `deepseek-harness`, `winui3`, `webview2`, `windows-desktop`, `fluent-design`, and `dotnet`.

The README will use this information architecture:

1. Project title, concise value proposition, platform/release/license badges, and an application screenshot.
2. Chinese overview and key features.
3. Download and installation instructions, including custom installation paths and offline dependencies.
4. Usage, tray behavior, data locations, and uninstall behavior.
5. Architecture and process lifecycle.
6. Harness update validation, rollback, and security boundaries.
7. Development requirements, build commands, tests, and release packaging.
8. Troubleshooting, including logs and expected first-start behavior after the race fix.
9. Known resource-budget limitations and project scope.
10. English summary with installation, architecture, development, and license essentials.
11. Attribution and license links.

The screenshot will be stored under `docs/images/` and referenced with a relative path so it renders on GitHub.

## First-launch Reliability Fix

The installed application can currently fail when official Harness profile dependencies are linked into `~/.dsh/profiles/node_modules` during the same Node process that first attempted to resolve them. The packages and junctions are valid after that process exits, and a new process starts successfully.

The shell will handle this narrow failure mode without changing or deleting `~/.dsh`:

- Detect an early Harness exit whose captured output contains both `ERR_MODULE_NOT_FOUND` and an import origin under the configured `.dsh/profiles` directory.
- Retry the same Harness version once in a fresh Node process after a short bounded delay.
- Never loop indefinitely and never retry unrelated failures automatically.
- Preserve the original and retry diagnostics in the redacted LocalWhale log.
- Reset the active version's failed-start counter after a successful start.
- Keep the existing recovery page when the single retry also fails.

This behavior will be covered by unit or integration tests that reproduce the first-process failure and second-process success.

## Continuous Integration

A GitHub Actions workflow will run on pushes and pull requests to `main` using a Windows runner. It will:

- install the .NET SDK version pinned by `global.json`;
- run the LocalWhale Core test suite in Release configuration;
- install a supported Node.js runtime;
- run the bridge tests with `npm test`;
- build the WinUI application in Release configuration without producing or uploading the offline installer.

The workflow will not download the bundled Harness runtime or publish release assets.

## Licensing and Attribution

- Add a root `LICENSE` containing the MIT License for LocalWhale.
- Retain `licenses/DeepSeek-Harness-LICENSE.txt` for the bundled upstream Harness.
- Retain and update `licenses/THIRD-PARTY-NOTICES.md` as necessary.
- State clearly that LocalWhale is an independent community project and is not affiliated with or endorsed by DeepSeek.
- Link to the official DeepSeek Harness repository and preserve upstream attribution.

## Publication Flow

1. Audit ignored and staged files for credentials, local paths, logs, runtime payloads, and generated artifacts.
2. Implement and test the first-launch retry and failed-start reset.
3. Add the MIT license, bilingual README, screenshot, and CI workflow.
4. Run all .NET and bridge tests, build the WinUI project, rebuild the release installer, and repeat a clean custom-path install/start/uninstall test.
5. Commit the complete intended source scope on the feature branch and fast-forward `main`.
6. Install and authenticate GitHub CLI if necessary.
7. Create the public `LocalWhale` repository, push `main`, set the description and topics, and verify repository visibility.
8. Create GitHub Release `v0.1.0`, upload the installer and a checksum file, and verify both assets are downloadable.

## Validation and Safety

- The repository must contain no files ignored by `.gitignore` and no secrets detected by focused source scans.
- The public tree must not contain absolute local installation paths, user-profile paths, bridge tokens, API keys, or credentials except benign examples and test fixtures.
- Core and bridge tests must pass with zero failures; the WinUI Release build must complete with zero errors.
- The final installer must contain the required PRI/XBF resources, stay below the 500 MiB unpacked limit, and install to a path containing spaces and non-ASCII characters.
- Complete exit must leave no scoped LocalWhale or bundled Node processes.
- Uninstall must delete LocalWhale-owned data while preserving `~/.dsh` byte-for-byte for the sampled credential file.
- The GitHub repository must be public, use `main`, show the MIT license, and expose the `v0.1.0` release assets.

## Known Limits for v0.1.0

The README will report rather than conceal the current resource measurements: the shell private-memory and combined idle working-set targets were not met on the development machine, while startup time, Node private memory, idle CPU, process cleanup, and installation-size targets were met. These measurements are environment-specific and are not presented as universal benchmarks.
