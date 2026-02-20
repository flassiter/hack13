-- Hack13 RPA Framework - Loan Database Schema
-- PostgreSQL 16

CREATE TABLE IF NOT EXISTS loans (
    loan_number              VARCHAR(10)    PRIMARY KEY,
    borrower_name            VARCHAR(100)   NOT NULL,
    co_borrower_name         VARCHAR(100),
    property_address         VARCHAR(150)   NOT NULL,
    city                     VARCHAR(50)    NOT NULL,
    state                    CHAR(2)        NOT NULL,
    zip_code                 VARCHAR(10)    NOT NULL,
    program_type             VARCHAR(20)    NOT NULL,   -- conventional, fha, va, usda
    status                   VARCHAR(20)    NOT NULL,   -- current, delinquent_30, delinquent_60, delinquent_90, foreclosure, paid_off
    loan_type                CHAR(1)        NOT NULL,   -- A=active, R=recovery/bankruptcy
    loan_status              CHAR(1)        NOT NULL,   -- A=active, R=recovery ready (mirrors loan_type)
    statute_of_limitations   INTEGER,                   -- days allowed for recovery; populated only when loan_type = 'R'
    assigned_to              VARCHAR(100),              -- recovery team member assigned to this loan; populated only when loan_type = 'R'
    bucket_id                VARCHAR(50),               -- recovery bucket assignment; populated only when loan_type = 'R'
    original_loan_amount     NUMERIC(12,2)  NOT NULL,
    current_balance          NUMERIC(12,2)  NOT NULL,
    interest_rate            NUMERIC(5,3)   NOT NULL,
    monthly_payment          NUMERIC(10,2)  NOT NULL,
    escrow_balance           NUMERIC(10,2)  NOT NULL,
    origination_date         DATE           NOT NULL,
    maturity_date            DATE           NOT NULL,
    last_payment_date        DATE,
    next_payment_due         DATE,
    property_value           NUMERIC(12,2)  NOT NULL,
    ltv_ratio                NUMERIC(5,2)   NOT NULL,

    CONSTRAINT chk_loan_type        CHECK (loan_type IN ('A', 'R')),
    CONSTRAINT chk_loan_status      CHECK (loan_status IN ('A', 'R')),
    CONSTRAINT chk_sol_with_type        CHECK (loan_type = 'R' OR statute_of_limitations IS NULL),
    CONSTRAINT chk_assigned_with_type  CHECK (loan_type = 'R' OR assigned_to IS NULL),
    CONSTRAINT chk_status_matches   CHECK (loan_type = loan_status)
);

CREATE INDEX IF NOT EXISTS idx_loans_status      ON loans (status);
CREATE INDEX IF NOT EXISTS idx_loans_loan_type   ON loans (loan_type);
CREATE INDEX IF NOT EXISTS idx_loans_loan_status ON loans (loan_status);
CREATE INDEX IF NOT EXISTS idx_loans_state       ON loans (state);
