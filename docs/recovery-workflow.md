# Recovery Loan Bucketing Workflow

## Background

- Need a nightly process for automatically "bucketing" all recovery-ready loans.
- Each rep has a "bucket" that represents their backlog of loans to work.
- Loans are categorized into 6 categories, and they need to be balanced across all the bucket in terms of total principal balance.
- The categories are:
    - chargeoff holding title
    - chargeoff holding lien
    - chargeff consumer lending
    - repo foreclosure dot 2/3
    - repo judgement

## High-level nightly workflow
- Get all recovery-ready loans (about 100 per 24 hours)
- For each loan:
    - Determine the loan category
    - Identify the buckets that are candidates for assignment, i.e. that matching these criteria:
        - Language (English, Spanish)
        - Collector is licensed in state
    - For each candidate bucket, consider total principal balance of its loans in the same category, and pick the bucket with the lowest amount
    - Assign the loan to the identified bucket
- Assigned loans are rolling over from day to day, and are not reassigned. However, previously assigned principal balance totals are considered when balancing assignments of new loans.

## Additional Requirements

- For solution flexibility, language and license can be genericized as skills, where a loan requires the skills "Spanish" and "Licensed in TN", and only certain collectors have those skills.
- The business probably needs to be able to configure exceptions, such to "pausing" a collectors availability due to PTO. 

## Workflow Definition

The top-level workflow file declares the pipeline. Each step names a component type and points to an external JSON config file for that step's settings.

Business-configurable parts:
- The true/false rule that defines a match between a collector/bucket and a loan
- The rule needs a number of fields to possible to evaluate, that come from multiple entities
- How do the steps orchestrate fething the correct for the rule? Or do they fetch "all" attributes? Or is that separate configuration for the workflow component that has to be done?

**File:** `configs/workflows/escrow_statement_generation.json`

Example bucket data - flattened to one per category:
```json
[
    {
        "collectorId": 123,
        "category": "Charge-off - Holding Title",
        "balance": 45454.12,
        "skills": [
            {
                "skillType": "language",
                "values": ["English", "Spanish"]
            },
            {
                "skillType": "recoveryLicense",
                "values": ["TN", "TX"]
            }
        ]
    },
    {
        "collectorId": 123,
        "category": "Charge-off - Holding Lien",
        "balance": 45454.12,
        "skills": [
            {
                "skillType": "language",
                "values": ["English", "Spanish"]
            },
            {
                "skillType": "recoveryLicense",
                "values": ["TN", "TX"]
            }
        ]
    }
]
```

Example loan data:
```json
[
    {
        "loanNumber": "123456-0",
        "principalBalances":123123.12,
        "category": "Charge-off - Holding Title",
        "language": "English"
    },
    {
        "loanNumber": "223456-0",
        "principalBalance": 223123.12,
        "category": "Charge-off - Holding Lien",
        "language": "Spanish"
    }
]
```

Example rule match definition:
```
for-each @loans l
    filter @buckets b
         b.skills[@skillType='language'] contains l.language
         b.skills[@skillType='recoveryLicense'] contains l.state
         b.category = l.category
    order-by-desc b.balance
    first
```

Workflow Definition:
```json
{
  "workflow_id": "recovery_loan_bucket_assignment",
  "workflow_version": "1.0",
  "description": "Fetch recovery-ready loans, assigned them to colletor buckets",
  "initial_parameters": [],
  "steps": [
    {
      "step_name": "fetch_recovery_ready_loans",
      "component_type": "data",
      "component_config": "../components/....json",
      "on_failure": "abort",
      "retry": { "max_attempts": 2, "backoff_seconds": 1 }
    },
    {
      "step_name": "fetch_current_bucket_balances",
      "component_type": "data",
      "component_config": "../components/....json",
      "on_failure": "abort",
      "retry": { "max_attempts": 2, "backoff_seconds": 1 }
    },
    {
        "step_name": "match_loans_to_buckets"
    },
    {
        "step_name": "upload_assignments_to_iseries",
    },
    {
        "step_name": "send_report"
    }
}
```

## Misc notes

loan type code: consumer lendinging, government servcing, commercial


## Other ideas

- proof of claim
- statue of limitations