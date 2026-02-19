# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- Workflow orchestrator hardening for unexpected step exceptions (`STEP_EXCEPTION`) and cleaner failure handling
- API improvements:
  - `POST /api/workflows/{workflowId}/execute` now returns clean `400` messages for validation/runtime errors
  - `GET /health` endpoint for service checks
- Frontend failure panel now surfaces failed step name and error details
- Demo preparation assets:
  - `docs/demo-script.md`
  - `docs/step-functions-migration.md`
- Optional containerized demo stack:
  - `docker-compose.yml`
  - Dockerfiles for API, mock server, and frontend (`src/Hack13.Api/Dockerfile`, `src/Hack13.TerminalServer/Dockerfile`, `frontend/Dockerfile`)
  - frontend nginx config for API proxy (`frontend/nginx.conf`)

## [0.3.0] - 2026-02-18

### Added

- **GreenScreenConnector** — `IComponent` implementation that connects to a TN5250 host, executes a JSON-configured workflow, and returns scraped screen data
- **TN5250 client protocol layer** — client-side telnet negotiation, 5250 data stream parser (handles `ClearUnit`, `WriteToDisplay`, `SBA`, `SF`, `RA`, `IC` orders), 24×80 screen buffer with field metadata, and an input encoder that builds GDS-framed output records
- **Screen identification** — position-based screen detection using shared screen catalog definitions
- **Workflow engine** — executes sequences of `Navigate`, `Assert`, and `Scrape` steps with `{{placeholder}}` resolution, configurable per-step timeouts, and retry support
- **Escrow lookup workflow** (`configs/workflows/escrow-lookup.json`) — 7-step configuration that signs on, enters a loan number, and scrapes 23 canonical fields across the Loan Details and Escrow Analysis screens
- **39 new tests** — 32 unit tests covering the screen buffer, data stream parser, input encoder, and screen identifier; 7 integration tests running end-to-end workflows against the mock server (including concurrent clients, invalid credentials, and invalid loan numbers)

## [0.2.0] - 2026-02-18

### Added

- **TN5250 mock server** — headless AS400 simulator for testing (`Hack13.TerminalServer`)
- Server-side TN5250 protocol layer — telnet negotiation, data stream writer (fluent builder), data stream reader, EBCDIC converter
- Screen rendering engine — converts screen catalog definitions and session data into 5250 data stream records
- Field extractor — maps modified client input fields to named screen fields by position
- Navigation engine — state machine driven by declarative transition rules in `configs/navigation.json`, with credential and loan-exists validation
- Four simulated screens — Sign On, Loan Inquiry, Loan Details, Escrow Analysis — defined as JSON in `configs/screen-catalog/`
- Three test loan records in `test-data/loans.json` (`1000001`, `1000002`, `1000003`)
- 64 tests for the mock server (protocol framing, screen rendering, field extraction, navigation logic)

## [0.1.0] - 2026-02-18

### Added

- .NET 8 solution scaffold with 10 source projects and 9 test projects
- **`Hack13.Contracts`** shared library — `IComponent` interface, `ComponentConfiguration`, `ComponentResult`, `WorkflowDefinition`, `WorkflowStep`, `ComponentStatus`/`FailurePolicy`/`LogLevel` enums
- **`PlaceholderResolver`** — resolves `{{key}}` tokens in template strings
- **`NumericParser`** — parses financial strings to `decimal` (currency symbols, commas, parentheses-as-negative)
- **`DataDictionaryExtensions`** — typed getters and setters on `Dictionary<string, string>`
- **`ScreenCatalog`** models — `ScreenDefinition`, `ScreenIdentifier`, `FieldDefinition`, `StaticTextElement`
- 20 tests for Contracts utilities
