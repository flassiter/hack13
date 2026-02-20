# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- **`Hack13.Database.Common`** — shared library that houses `DbConnectionFactory` (SQL Server, PostgreSQL, MySQL, SQLite); referenced by both `DatabaseReader` and `DatabaseWriter` to eliminate duplication
- **`Hack13.DatabaseWriter`** — `IComponent` that executes a parameterized SQL write command (INSERT/UPDATE/DELETE) or scalar query; supports `{{placeholder}}` resolution in connection string, query, and parameter values; writes rows-affected or scalar result to a configurable `output_key`; error codes: `CONFIG_ERROR`, `UNSUPPORTED_PROVIDER`, `CONNECTION_ERROR`, `QUERY_ERROR`
- **`Hack13.HttpClient`** — `IComponent` that sends an HTTP request to a REST API; supports `{{placeholder}}` resolution in URL, headers, and body; configurable success status codes; maps dot-notation JSON paths from the response body into the data dictionary; always writes `http_status_code`; error codes: `CONFIG_ERROR`, `REQUEST_FAILED`, `HTTP_ERROR`, `RESPONSE_PARSE_ERROR`
- **`Hack13.ApprovalGate`** — `IComponent` that polls a REST approval endpoint on a fixed interval until an approved or rejected signal is detected or a wall-clock timeout is reached; tolerates transient HTTP errors; always writes `approval_status` and `approval_poll_count`; error codes: `CONFIG_ERROR`, `REJECTED`, `TIMEOUT`
- Three new components registered in `ComponentRegistry.CreateDefault()`: `database_writer`, `http_client`, `approval_gate`
- Example component configs: `configs/components/db_write_bucket_assignment.json`, `http_post_letter_queue.json`, `approval_gate_letter.json`
- Workflow orchestrator hardening for unexpected step exceptions (`STEP_EXCEPTION`) and cleaner failure handling
- **`Hack13.DatabaseReader`** — `IComponent` that executes a parameterized SQL query and writes results into the data dictionary; supports single-row and multi-row (JSON array) output modes, configurable `output_prefix`, `require_row` guard, and `SqlServer`/`SQLite` providers
- **`foreach` step type** — iterates over a JSON-serialized row list in the data dictionary and executes a sequence of `sub_steps` for each row; enables database-driven bulk processing workflows (e.g. `db_loan_lookup.json`)
- API improvements:
  - `GET /api/workflows` — lists all workflow files with rich metadata: description, version, component types, PDF templates, step names, last-modified timestamp, and parse-error details
  - `POST /api/workflows/{workflowId}/execute` now returns clean `400` messages for validation/runtime errors
  - `GET /api/workflows/{workflowId}/execute-stream` — SSE streaming endpoint that emits `progress` events per step (state, attempt, message), a `summary` event on completion, and a `workflow_error` event on fatal failure
  - `GET /api/workflows/{workflowId}/definition` — returns the raw workflow JSON
  - `PUT /api/workflows/{workflowId}/definition` — validates and persists updated workflow JSON to disk
  - `GET /api/files/pdf?path=...` — serves generated PDF files from the `output/` directory
  - `GET /health` endpoint for service checks
- **Frontend — Workflow Runner** enhancements:
  - Workflow dropdown now populated from `GET /api/workflows` with metadata display (description, version, components, PDF templates, last modified)
  - Real-time step progress via SSE; parameter inputs dynamically generated from `initial_parameters`
  - Parse-error detection for invalid workflow files surfaced inline
  - Result panel shows PDF download link, email delivery status, and failed-step error details
- **Frontend — Workflow Catalog** page:
  - Two-pane read-only browser: left pane lists all workflows with name, description, and step count; right pane shows full metadata (version, last modified, initial parameters, step names, component types, PDF templates) plus a collapsible raw JSON view
  - Parse errors in workflow files are surfaced inline so misconfigured definitions are visible without running them
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
