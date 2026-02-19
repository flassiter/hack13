# Recovery Letter Campaign

Targeted mail campaign to attempt to get customer to settle principal balance

## Workflow Overview 
- Nightly Trigger: Find all loans that match these configurable criteria:
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
            - Discount tier (10, 20, 30%)
            - Settlement offer
    - Build data file
    - Schedule letter [@LetterId = 'RecoveryLetter', @Schedule = 'ASAP', @Data = data]

- On Letter Queue item matured:
    - Generate letter

- On Letter Generated
    - Schedule Letter Delivery [@DocumentId = e.DocumentId, DeliveryChannel = 'PostalMail', DeliveryProvider = 'MPC', @ManualApproval = true]

## Business-Configurable Rules
There is a DSL for deterministic rules. However, natural language can be used together with field metadata to assist in drafting those rules.

- Field/value pairs for customers that should get letters: 
    - Natural language: `Loans in recovery status that have been in recovery for over two years`
    - DSL: `loan.status = 'R' and loan.recoveryStartDate < currentDate - 24 months`
    - Conversion of DSL to WHERE clause fragment for data reader
- Field/value/threshold tuples for determining discount tier/percentage:
    - Natural language: `If the loan has less than six months before statute of limitations, then the discount should be 40%. Otherwise, it is based on months the loan has been in recovery: Less than two years is 10%, less than four years is 20%, anything else is 30%.`
    - DSL: `if loan.statuteOfLimitations < 6 then 40 else if loan.monthsInRecovery < 25 then 10 else if loan.monthsInRecovery < 48 then 20 else 30 end`
- Expression for calculating settlment offer, e.g.:
    - Natural language: `Loan principal minus discount`
    - DSL: `loan.principalBalance - loan.principalBalance * discountPercentage`
