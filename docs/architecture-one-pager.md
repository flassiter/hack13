# RPA POC: Configuration-Driven Architecture

## What It Is

A .NET 8 Robotic Process Automation engine that connects to legacy AS400 green-screen systems, extracts mortgage servicing data, and runs multi-step business workflows — all defined entirely in JSON configuration files. No code changes are needed to add new workflows, modify business rules, or change output templates.

## Core Concept: Everything is Configuration

The system separates **what to do** (JSON config files) from **how to do it** (compiled components). Business analysts define workflows, screens, calculations, rules, and templates as JSON. Developers build and register reusable components once. The orchestrator wires them together at runtime.

```
configs/
├── workflows/           Workflow pipelines — which steps run, in what order
├── components/          Per-step settings — connection details, calculations, rules
├── screen-catalog/      Green-screen field maps — row, column, field name, length
└── templates/           HTML templates for PDF generation and email bodies
```

## Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                          ORCHESTRATOR                                │
│   Loads workflow JSON ──► Resolves {{placeholders}} ──► Sequences    │
│   steps ──► Routes to registered component ──► Merges output into   │
│   shared Data Dictionary ──► Applies retry / failure policies        │
└──────────┬───────────────────────────────────────────────────────────┘
           │ creates per-step
           ▼
┌────────────────────────────────────────────────────────────────┐
│                     COMPONENT REGISTRY                         │
│                                                                │
│  "green_screen_connector"  ──►  GreenScreenConnector           │
│  "calculate"               ──►  CalculatorComponent            │
│  "decision"                ──►  DecisionEngineComponent        │
│  "pdf_generator"           ──►  PdfGeneratorComponent          │
│  "email_sender"            ──►  EmailSenderComponent           │
│                                                                │
│  Any new component: implement IComponent, register by name     │
└────────────────────────────────────────────────────────────────┘
```

### The IComponent Contract

Every component implements one interface:

```csharp
public interface IComponent
{
    string ComponentType { get; }
    Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken ct);
}
```

- **ComponentConfiguration** — wraps the component type string and a `JsonElement` holding the component-specific JSON settings.
- **Data Dictionary** — a `Dictionary<string, string>` shared across all steps. Each component reads its inputs from the dictionary and writes its outputs back. This is the pipeline that connects steps to each other.
- **ComponentResult** — returns `Success`/`Failure`/`Skipped`, output key-value pairs, error details, and log entries.

## How a Workflow Executes

Using the `escrow_statement_generation` workflow as an example:

```
                         Data Dictionary (shared state)
                         ┌────────────────────────┐
 Inputs: loan_number,    │ loan_number: 1000001   │
 host, credentials ────► │ host: localhost         │
                         │ user_id: TESTUSER       │
                         └──────────┬─────────────┘
                                    │
    Step 1: green_screen_connector  │  Sign on to AS400, navigate screens,
    Config: escrow_lookup.json      │  scrape 23 loan + escrow fields
                                    ▼
                         ┌────────────────────────┐
                         │ + borrower_name         │
                         │ + escrow_balance        │
                         │ + required_reserve ...  │
                         └──────────┬─────────────┘
                                    │
    Step 2: calculate               │  Compute shortage/surplus and
    Config: escrow_shortage_calc    │  adjusted monthly payment
                                    ▼
                         ┌────────────────────────┐
                         │ + escrow_shortage_surp  │
                         │ + monthly_escrow_adj    │
                         │ + adjusted_monthly_pmt  │
                         └──────────┬─────────────┘
                                    │
    Step 3: decision                │  First-match rule evaluation:
    Config: escrow_notice_decision  │  selects template + email priority
                                    ▼
                         ┌────────────────────────┐
                         │ + notice_type           │
                         │ + pdf_template          │
                         │ + email_priority        │
                         └──────────┬─────────────┘
                                    │
    Step 4: pdf_generator           │  Renders HTML template to PDF
    Config: escrow_pdf_config       │  using data dictionary values
                                    ▼
                         ┌────────────────────────┐
                         │ + pdf_file_path         │
                         └──────────┬─────────────┘
                                    │
    Step 5: email_sender            │  Sends email with PDF attachment
    Config: escrow_email_config     │  (on_failure: log_and_continue)
                                    ▼
                              Workflow Complete
```

## Configuration Layers in Detail

### 1. Workflow Definition (what steps to run)

```json
{
  "workflow_id": "escrow_statement_generation",
  "steps": [
    {
      "step_name": "lookup_escrow_data",
      "component_type": "green_screen_connector",
      "component_config": "../components/escrow_lookup.json",
      "on_failure": "abort",
      "retry": { "max_attempts": 2, "backoff_seconds": 1 }
    }
  ]
}
```

Each step declares its component type, points to an external config file, and specifies failure/retry policy. Steps can also have conditions for conditional execution.

### 2. Component Configuration (how each step behaves)

Each component's JSON file controls its behavior without code changes:

| Component | Configured via JSON |
|---|---|
| **GreenScreenConnector** | Connection settings, screen navigation steps (Navigate/Assert/Scrape), field mappings |
| **Calculator** | Named calculations with operation (add/subtract/divide), input keys, output key, formatting |
| **DecisionEngine** | Ordered rules with field/operator/value conditions and output key-value pairs |
| **PdfGenerator** | Template ID (resolved dynamically), template registry path, output directory |
| **EmailSender** | From/to/subject/body template, attachments — all support `{{placeholder}}` substitution |

### 3. Screen Catalog (how to read AS400 screens)

Each screen is a JSON file mapping field names to exact row/column positions:

```json
{
  "screen_id": "loan_details",
  "identifier": { "row": 1, "col": 25, "expected_text": "Loan Details" },
  "fields": [
    { "name": "borrower_name", "type": "display", "row": 5, "col": 35, "length": 30 }
  ]
}
```

Screens are identified automatically at runtime by matching text at known positions. To add a new screen, drop in a new JSON file — no code changes.

### 4. Placeholder Resolution (dynamic values everywhere)

`{{key}}` placeholders in any configuration string are resolved against the data dictionary at runtime. This is what connects steps together — Step 3's decision engine writes `pdf_template`, and Step 4's PDF generator reads `{{pdf_template}}` to select the right template. The pipeline is implicit and configuration-driven.

## Failure Handling (also configurable)

Each workflow step declares its failure policy:

| Policy | Behavior |
|---|---|
| `abort` | Stop the workflow immediately |
| `log_and_continue` | Log the error, continue to the next step |
| `retry` | Retry with configurable max attempts and backoff |

## Entry Points

| Interface | Description |
|---|---|
| **CLI** | `dotnet run --project src/Hack13.Cli -- --workflow <path> --param key=value` |
| **REST API** | `POST /api/workflows/{id}/execute` with JSON parameters |
| **Web Frontend** | React UI that calls the API and displays real-time step progress |

## Key Design Decisions

- **No hardcoded business logic** — calculations, rules, screen maps, and templates are all JSON.
- **Components are stateless** — all state flows through the data dictionary, making components reusable and testable in isolation.
- **Extensible by registration** — new component types are added by implementing `IComponent` and calling `registry.Register("name", factory)`.
- **Mock-friendly** — the included TN5250 mock server loads its screen layouts and navigation rules from the same `configs/` directory, enabling end-to-end testing without a real AS400.
