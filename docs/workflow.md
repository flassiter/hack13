# Escrow Statement Generation Workflow

This document walks through the complete escrow lookup and email workflow, showing the JSON configuration that drives each step. The workflow connects to a legacy AS400 green-screen system, scrapes loan and escrow data, calculates shortage/surplus amounts, selects the appropriate notice template, generates a PDF statement, and emails it to the borrower.

## Workflow Definition

The top-level workflow file declares the pipeline. Each step names a component type and points to an external JSON config file for that step's settings.

**File:** `configs/workflows/escrow_statement_generation.json`

```json
{
  "workflow_id": "escrow_statement_generation",
  "workflow_version": "1.0",
  "description": "Look up escrow data, calculate shortage/surplus, choose templates, generate PDF, and email borrower",
  "initial_parameters": ["loan_number"],
  "steps": [
    {
      "step_name": "lookup_escrow_data",
      "component_type": "green_screen_connector",
      "component_config": "../components/escrow_lookup.json",
      "on_failure": "abort",
      "retry": { "max_attempts": 2, "backoff_seconds": 1 }
    },
    {
      "step_name": "calculate_shortage",
      "component_type": "calculate",
      "component_config": "../components/escrow_shortage_calc.json",
      "on_failure": "abort"
    },
    {
      "step_name": "determine_notice_type",
      "component_type": "decision",
      "component_config": "../components/escrow_notice_decision.json",
      "on_failure": "abort"
    },
    {
      "step_name": "generate_pdf",
      "component_type": "pdf_generator",
      "component_config": "../components/escrow_pdf_config.json",
      "on_failure": "abort"
    },
    {
      "step_name": "send_email",
      "component_type": "email_sender",
      "component_config": "../components/escrow_email_config.json",
      "on_failure": "log_and_continue"
    }
  ]
}
```

**Key points:**
- `initial_parameters` declares what the caller must provide (at minimum, `loan_number`).
- Each step's `on_failure` policy controls what happens if that step fails: `abort` stops the workflow, `log_and_continue` records the error and moves on.
- The email step uses `log_and_continue` so a delivery failure doesn't discard all the work done by prior steps.
- The green screen step has `retry` configured — if the connection drops, it will retry once after a 1-second backoff.

---

## Step 1: Lookup Escrow Data (Green Screen Connector)

This step opens a TN5250 connection to the AS400, signs on, navigates to the loan, and scrapes data from two screens.

**File:** `configs/components/escrow_lookup.json`

```json
{
  "component_type": "green_screen_connector",
  "component_version": "1.0",
  "description": "Lookup escrow details from green-screen system",
  "config": {
    "connection": {
      "host": "{{host}}",
      "port": 5250,
      "connect_timeout_seconds": 5,
      "response_timeout_seconds": 10
    },
    "screen_catalog_path": "{{screen_catalog_path}}",
    "steps": [
      {
        "step_name": "sign_on",
        "type": "Navigate",
        "fields": { "user_id": "{{user_id}}", "password": "{{password}}" },
        "aid_key": "Enter",
        "expect_screen": "loan_inquiry"
      },
      {
        "step_name": "verify_sign_on",
        "type": "Assert",
        "expect_screen": "loan_inquiry",
        "error_text": "Invalid user ID or password",
        "error_row": 24
      },
      {
        "step_name": "enter_loan_number",
        "type": "Navigate",
        "fields": { "loan_number": "{{loan_number}}" },
        "aid_key": "Enter",
        "expect_screen": "loan_details"
      },
      {
        "step_name": "verify_loan_details",
        "type": "Assert",
        "expect_screen": "loan_details",
        "error_text": "Loan not found",
        "error_row": 24
      },
      {
        "step_name": "scrape_loan_details",
        "type": "Scrape",
        "screen": "loan_details",
        "scrape_fields": [
          "borrower_name", "property_address", "loan_type",
          "original_amount", "current_balance", "interest_rate",
          "monthly_payment", "next_due_date", "loan_status",
          "origination_date"
        ]
      },
      {
        "step_name": "go_to_escrow",
        "type": "Navigate",
        "aid_key": "F6",
        "expect_screen": "escrow_analysis"
      },
      {
        "step_name": "scrape_escrow_analysis",
        "type": "Scrape",
        "screen": "escrow_analysis",
        "scrape_fields": [
          "escrow_balance", "escrow_payment", "required_reserve",
          "shortage_amount", "surplus_amount", "escrow_status",
          "tax_amount", "hazard_insurance", "flood_insurance",
          "mortgage_insurance", "last_analysis_date",
          "next_analysis_date", "projected_balance"
        ]
      }
    ]
  }
}
```

### How Screen Navigation Works

The connector executes its inner `steps` array sequentially over a single TN5250 session. There are three step types:

- **Navigate** — Fills input fields on the current screen, presses an AID key (Enter, F6, etc.), and verifies the resulting screen matches `expect_screen`.
- **Assert** — Checks the current screen ID and verifies that error text does not appear at a specific row.
- **Scrape** — Reads field values from the screen buffer at positions defined in the screen catalog.

The navigation path through the AS400 looks like this:

```
Sign On ──Enter──► Loan Inquiry ──Enter──► Loan Details ──F6──► Escrow Analysis
          (credentials)          (loan #)     (scrape 10)         (scrape 13)
```

### Screen Catalog

The connector identifies which screen it's on and where fields are located using screen catalog JSON files. Each screen has an identifier (text at a known row/column) and a list of fields with their exact positions.

**File:** `configs/screen-catalog/sign_on.json`

```json
{
  "screen_id": "sign_on",
  "identifier": { "row": 1, "col": 28, "expected_text": "Sign On" },
  "fields": [
    { "name": "user_id",  "type": "input", "row": 10, "col": 35, "length": 10 },
    { "name": "password", "type": "input", "row": 12, "col": 35, "length": 10, "attributes": "hidden" }
  ]
}
```

**File:** `configs/screen-catalog/loan_inquiry.json`

```json
{
  "screen_id": "loan_inquiry",
  "identifier": { "row": 1, "col": 25, "expected_text": "Loan Inquiry" },
  "fields": [
    { "name": "loan_number", "type": "input", "row": 8, "col": 35, "length": 10 }
  ]
}
```

**File:** `configs/screen-catalog/loan_details.json`

```json
{
  "screen_id": "loan_details",
  "identifier": { "row": 1, "col": 25, "expected_text": "Loan Details" },
  "fields": [
    { "name": "loan_number",      "type": "display", "row": 4,  "col": 35, "length": 10 },
    { "name": "borrower_name",    "type": "display", "row": 5,  "col": 35, "length": 30 },
    { "name": "property_address", "type": "display", "row": 6,  "col": 35, "length": 40 },
    { "name": "loan_type",        "type": "display", "row": 8,  "col": 35, "length": 15 },
    { "name": "original_amount",  "type": "display", "row": 9,  "col": 35, "length": 15 },
    { "name": "current_balance",  "type": "display", "row": 10, "col": 35, "length": 15 },
    { "name": "interest_rate",    "type": "display", "row": 11, "col": 35, "length": 10 },
    { "name": "monthly_payment",  "type": "display", "row": 12, "col": 35, "length": 15 },
    { "name": "next_due_date",    "type": "display", "row": 13, "col": 35, "length": 10 },
    { "name": "loan_status",      "type": "display", "row": 14, "col": 35, "length": 12 },
    { "name": "origination_date", "type": "display", "row": 16, "col": 35, "length": 10 },
    { "name": "maturity_date",    "type": "display", "row": 17, "col": 35, "length": 10 },
    { "name": "escrow_balance",   "type": "display", "row": 19, "col": 35, "length": 15 },
    { "name": "escrow_status",    "type": "display", "row": 20, "col": 35, "length": 12 }
  ]
}
```

**File:** `configs/screen-catalog/escrow_analysis.json`

```json
{
  "screen_id": "escrow_analysis",
  "identifier": { "row": 1, "col": 23, "expected_text": "Escrow Analysis" },
  "fields": [
    { "name": "loan_number",        "type": "display", "row": 3,  "col": 35, "length": 10 },
    { "name": "borrower_name",      "type": "display", "row": 4,  "col": 35, "length": 30 },
    { "name": "escrow_balance",     "type": "display", "row": 6,  "col": 35, "length": 15 },
    { "name": "escrow_payment",     "type": "display", "row": 7,  "col": 35, "length": 15 },
    { "name": "required_reserve",   "type": "display", "row": 8,  "col": 35, "length": 15 },
    { "name": "shortage_amount",    "type": "display", "row": 10, "col": 35, "length": 15 },
    { "name": "surplus_amount",     "type": "display", "row": 11, "col": 35, "length": 15 },
    { "name": "escrow_status",      "type": "display", "row": 12, "col": 35, "length": 15 },
    { "name": "tax_amount",         "type": "display", "row": 14, "col": 35, "length": 15 },
    { "name": "hazard_insurance",   "type": "display", "row": 15, "col": 35, "length": 15 },
    { "name": "flood_insurance",    "type": "display", "row": 16, "col": 35, "length": 15 },
    { "name": "mortgage_insurance", "type": "display", "row": 17, "col": 35, "length": 15 },
    { "name": "last_analysis_date", "type": "display", "row": 19, "col": 35, "length": 10 },
    { "name": "next_analysis_date", "type": "display", "row": 20, "col": 35, "length": 10 },
    { "name": "projected_balance",  "type": "display", "row": 21, "col": 35, "length": 15 },
    { "name": "projected_shortage", "type": "display", "row": 22, "col": 35, "length": 15 }
  ]
}
```

### Data Dictionary After Step 1

After this step completes, 23 scraped fields are merged into the shared data dictionary and are available to all subsequent steps:

| Source Screen | Fields Added |
|---|---|
| Loan Details | `borrower_name`, `property_address`, `loan_type`, `original_amount`, `current_balance`, `interest_rate`, `monthly_payment`, `next_due_date`, `loan_status`, `origination_date` |
| Escrow Analysis | `escrow_balance`, `escrow_payment`, `required_reserve`, `shortage_amount`, `surplus_amount`, `escrow_status`, `tax_amount`, `hazard_insurance`, `flood_insurance`, `mortgage_insurance`, `last_analysis_date`, `next_analysis_date`, `projected_balance` |

---

## Step 2: Calculate Shortage/Surplus

This step performs three chained calculations using values scraped in Step 1.

**File:** `configs/components/escrow_shortage_calc.json`

```json
{
  "component_type": "calculate",
  "component_version": "1.0",
  "description": "Compute escrow shortage/surplus and payment adjustment",
  "config": {
    "calculations": [
      {
        "name": "shortage_surplus",
        "output_key": "escrow_shortage_surplus",
        "operation": "subtract",
        "inputs": ["escrow_balance", "required_reserve"],
        "format": { "decimal_places": 2 }
      },
      {
        "name": "monthly_adjustment",
        "output_key": "monthly_escrow_adjustment",
        "operation": "divide",
        "inputs": ["escrow_shortage_surplus", "12"],
        "format": { "decimal_places": 2 }
      },
      {
        "name": "adjusted_payment",
        "output_key": "adjusted_monthly_payment",
        "operation": "add",
        "inputs": ["monthly_payment", "monthly_escrow_adjustment"],
        "format": { "decimal_places": 2 }
      }
    ]
  }
}
```

Calculations execute in order. Each one reads inputs from the data dictionary (or uses literal values like `"12"`), performs the operation, and writes the result back under `output_key`. This means later calculations can reference earlier ones — `monthly_adjustment` uses the `escrow_shortage_surplus` computed by `shortage_surplus`.

### Data Dictionary After Step 2

Three new keys are added:

| Key | Calculation |
|---|---|
| `escrow_shortage_surplus` | `escrow_balance - required_reserve` |
| `monthly_escrow_adjustment` | `escrow_shortage_surplus / 12` |
| `adjusted_monthly_payment` | `monthly_payment + monthly_escrow_adjustment` |

---

## Step 3: Determine Notice Type (Decision Engine)

This step evaluates the calculated shortage/surplus and selects the appropriate notice type, PDF template, and email priority.

**File:** `configs/components/escrow_notice_decision.json`

```json
{
  "component_type": "decision",
  "component_version": "1.0",
  "description": "Select notice, PDF template, and email priority",
  "config": {
    "evaluation_mode": "first_match",
    "rules": [
      {
        "rule_name": "significant_shortage",
        "condition": {
          "field": "escrow_shortage_surplus",
          "operator": "less_than",
          "value": "-500"
        },
        "outputs": {
          "notice_type": "shortage_urgent",
          "pdf_template": "escrow_shortage_urgent",
          "email_template": "escrow_email.html",
          "email_priority": "high"
        }
      },
      {
        "rule_name": "minor_shortage",
        "condition": {
          "field": "escrow_shortage_surplus",
          "operator": "less_than",
          "value": "0"
        },
        "outputs": {
          "notice_type": "shortage_minor",
          "pdf_template": "escrow_shortage_minor",
          "email_template": "escrow_email.html",
          "email_priority": "normal"
        }
      },
      {
        "rule_name": "surplus",
        "condition": {
          "field": "escrow_shortage_surplus",
          "operator": "greater_than",
          "value": "0"
        },
        "outputs": {
          "notice_type": "surplus",
          "pdf_template": "escrow_surplus",
          "email_template": "escrow_email.html",
          "email_priority": "normal"
        }
      },
      {
        "rule_name": "even",
        "condition": {
          "field": "escrow_shortage_surplus",
          "operator": "equals",
          "value": "0"
        },
        "outputs": {
          "notice_type": "current",
          "pdf_template": "escrow_current",
          "email_template": "escrow_email.html",
          "email_priority": "normal"
        }
      }
    ]
  }
}
```

Rules are evaluated top-to-bottom in `first_match` mode — the first rule whose condition is true wins. This means a shortage of -$600 matches `significant_shortage` (not `minor_shortage`) because it appears first.

### Data Dictionary After Step 3

Four new keys are written by whichever rule matched:

| Key | Example Value (significant shortage) |
|---|---|
| `notice_type` | `shortage_urgent` |
| `pdf_template` | `escrow_shortage_urgent` |
| `email_template` | `escrow_email.html` |
| `email_priority` | `high` |

---

## Step 4: Generate PDF

This step renders an HTML template to PDF using data from the dictionary.

**File:** `configs/components/escrow_pdf_config.json`

```json
{
  "component_type": "pdf_generator",
  "component_version": "1.0",
  "description": "Generate escrow statement PDF",
  "config": {
    "template_id": "{{pdf_template}}",
    "template_registry_path": "configs/templates/template-registry.json",
    "output_directory": "output/pdfs"
  }
}
```

Notice `{{pdf_template}}` — this is resolved at runtime from the data dictionary. The decision engine in Step 3 wrote `pdf_template=escrow_shortage_urgent`, so this step will look up that template ID in the registry.

### Template Registry

The registry maps template IDs to HTML files and declares which data dictionary fields each template requires.

**File:** `configs/templates/template-registry.json` (abbreviated to one entry)

```json
{
  "templates": [
    {
      "template_id": "escrow_shortage_urgent",
      "file_path": "./escrow_shortage_urgent.html",
      "required_fields": [
        "borrower_name", "loan_number", "property_address",
        "escrow_balance", "required_reserve", "shortage_amount",
        "escrow_payment", "monthly_payment", "tax_amount",
        "hazard_insurance", "last_analysis_date", "next_analysis_date"
      ],
      "optional_fields": [
        "statement_date", "flood_insurance", "mortgage_insurance",
        "escrow_shortage_surplus", "monthly_escrow_adjustment",
        "adjusted_monthly_payment"
      ],
      "default_filename_pattern": "escrow_statement_{{loan_number}}_{{statement_date}}.pdf"
    }
  ]
}
```

The registry contains four templates: `escrow_shortage_urgent`, `escrow_shortage_minor`, `escrow_surplus`, and `escrow_current` — one for each decision rule outcome.

### Data Dictionary After Step 4

| Key | Description |
|---|---|
| `pdf_file_path` | File system path to the generated PDF |

---

## Step 5: Send Email

This step sends the generated PDF as an email attachment to the borrower.

**File:** `configs/components/escrow_email_config.json`

```json
{
  "component_type": "email_sender",
  "component_version": "1.0",
  "description": "Send escrow statement email",
  "config": {
    "from": "statements@example.com",
    "to": ["{{customer_email}}"],
    "subject": "Escrow Analysis Statement - Loan {{loan_number}}",
    "body_template": "{{email_template}}",
    "attachments": ["{{pdf_file_path}}"],
    "reply_to": "support@example.com"
  }
}
```

Every value with `{{...}}` is resolved from the data dictionary at runtime:
- `{{customer_email}}` — provided as an input parameter when the workflow was started.
- `{{loan_number}}` — provided as input and also scraped from the green screen.
- `{{email_template}}` — set by the decision engine in Step 3.
- `{{pdf_file_path}}` — set by the PDF generator in Step 4.

This step's failure policy is `log_and_continue`, so if email delivery fails (e.g., SMTP is down), the workflow still completes successfully and the generated PDF is preserved.

---

## How Data Flows Between Steps

The data dictionary is the shared pipeline connecting all steps. Each step reads values placed by earlier steps and writes new values for later steps to consume.

```
Caller provides:         loan_number, host, user_id, password,
                         customer_email, screen_catalog_path
                                    │
                                    ▼
Step 1 (Green Screen)    + borrower_name, current_balance, escrow_balance,
  writes 23 fields         required_reserve, monthly_payment, ...
                                    │
                                    ▼
Step 2 (Calculator)      + escrow_shortage_surplus, monthly_escrow_adjustment,
  writes 3 fields          adjusted_monthly_payment
                                    │
                                    ▼
Step 3 (Decision)        + notice_type, pdf_template, email_template,
  writes 4 fields          email_priority
                                    │
                                    ▼
Step 4 (PDF Generator)   + pdf_file_path
  writes 1 field                    │
                                    ▼
Step 5 (Email Sender)     reads pdf_file_path, customer_email, loan_number,
  writes 0 fields          email_template → sends email
```

No step has hardcoded knowledge of any other step. The connections between them are implicit through shared data dictionary keys and `{{placeholder}}` resolution in the configuration files.
