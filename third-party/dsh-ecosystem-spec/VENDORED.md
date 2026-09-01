# Vendored dsh-ecosystem-spec

Source: https://github.com/T-Auto/dsh-ecosystem-spec (MIT License, see `LICENSE` in this directory)

Vendored: 2026-09-01, spec baseline Community v0.15 / TUI Admission v0.15.

Included files:

- `README.md` — upstream overview of the DSH Community Ecosystem Interoperability Specification.
- `docs/plugin-admission-and-development.md` — the single integrated admission & development guide (manifest, host descriptor, compatibility rules, checklist).
- `PLUGIN-ADMISSION-CHECKLIST.md` — pointer to the checklist section of the guide above.
- `schemas/host-descriptor.schema.json` — JSON Schema for host descriptors.
- `conformance-fixtures/valid-plugin.json` — reference `dsh-plugin.json` fixture.
- `adapters/dsh-tui-v0.15.md` — upstream example of a host-version → contract adapter table.

How LocalWhale uses it: `src/LocalWhale.Core/Plugins/` implements the plugin manifest model
(`dsh-plugin.json`), the host descriptor model, and the five-state admission decision
(compatible / compatible-degraded / waiting-authorization / rejected / unknown) reduced from the
upstream conformance core. `HarnessContractProfile` is LocalWhale's adapter table mapping each
Harness (dsh) version to the contract coordinates the desktop host mediates; installed plugins
under the user's dsh home are evaluated against it whenever a Harness update is discovered or
validated. Update this vendored copy when bumping to a newer spec baseline.
