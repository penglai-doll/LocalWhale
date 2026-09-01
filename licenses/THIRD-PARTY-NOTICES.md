# Third-party notices

LocalWhale redistributes or builds upon the following components:

- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) `@deepseek-ai/dsh` 0.1.0-rc.6 — MIT License. The upstream license is reproduced in `DeepSeek-Harness-LICENSE.txt`.
- [Node.js](https://nodejs.org/) 24.18.1 — MIT License and the third-party notices distributed with Node.js.
- [pnpm](https://github.com/pnpm/pnpm) 11.7.0 — MIT License.
- [.NET](https://github.com/dotnet/runtime) 10 — MIT License and the notices included in the self-contained publish output.
- [Microsoft Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) 2.3.1 — Microsoft license terms and notices included with its redistributable files.
- [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) — Microsoft software license terms. The installer carries Microsoft's Evergreen standalone installer for offline setup when the runtime is absent.
- [Inno Setup](https://github.com/jrsoftware/issrc) 6 and its Simplified Chinese translation — Inno Setup license terms.
- [dsh-ecosystem-spec](https://github.com/T-Auto/dsh-ecosystem-spec) (DSH Community Ecosystem Interoperability Specification) 0.15 — MIT License. The vendored copy in `third-party/dsh-ecosystem-spec/` keeps the upstream LICENSE; LocalWhale implements its plugin manifest and host-descriptor admission model so installed dsh plugins are evaluated against each Harness version.

DeepSeek Harness retains its own data directory (`~/.dsh`). LocalWhale does not copy, export, or delete that directory during uninstall.
