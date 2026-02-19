# RPA Enablement Framework

This framework enables Tech and Business stakeholders to collaborate on automating business processes. A workflow engine uses an extensible library of components to perform calculations, decisions and actions. It can be triggered by an event in a system, on a schedule, or manually through an admin console.

The admin console features an AI Assistant that helps the user describe, validate, visualize, edit and debug workflows. 

## Example Use Case: Recovery Letter Campaign

Targeted mail campaign to attempt to get customers in recovery to settle principal balance.

## Workflow Overview 
- Nightly schedule trigger: Kick off Recovery Letter workflow:
    - Find all loans that match these configurable criteria:
        - Loan in Recovery status
        - Loan in Recovery for more than x months
        - Letter not sent in last y months
        - Trigger Send Letter to Customer Event [@LoanNumber = '12345-0', @EventType = 'RecoveryLetter']

    - On Send Letter Trigger fired:
        - Gather fields for letter:
            - Loan Number
            - Customer Name, Address
            - Principal
            - Months in recovery
            - Time left before Statute of Limitations
            - Use configurable rules to determine:
                - Discount tier (e.g. 10, 20, 30%)
                - Settlement offer
        - Build data file
        - Schedule letter [@TemplateId = 'RecoveryLetter', @DeliveryChannel = 'PostalMail', @DeliveryProvider = 'MPC', @ManualApproval = true, @Schedule = 'ASAP', @Data = data]
            - API call to Letter Queue system

    - In the Letter Queue system:
        - On Letter Queue item created:
            - Generate letter
            - Request Approval

        - On Letter Approved
            - Delivery steps (print, ship)

## Business-Configurable Rules

All workflows and component invocations are configuration-driven with deterministic execution. An AI assistant helps craft and validate the configuration.

In the above workflow example, the business could easily and quickly update:
- Loan search criteria for which recovery letters should be generated.
- Decision criteria for determining settlement discount tier to be offered.
- Letter template (as part of the Letter Queue project)
