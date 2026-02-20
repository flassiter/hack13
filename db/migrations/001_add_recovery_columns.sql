-- Migration 001: Add recovery columns (assigned_to, bucket_id) and constraint
-- Idempotent: safe to run on both fresh and existing databases.
-- Fresh volumes already have these via 01_schema.sql; this script brings
-- existing volumes up to date without data loss.

ALTER TABLE loans ADD COLUMN IF NOT EXISTS assigned_to VARCHAR(100);
ALTER TABLE loans ADD COLUMN IF NOT EXISTS bucket_id   VARCHAR(50);

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_assigned_with_type'
    ) THEN
        ALTER TABLE loans
            ADD CONSTRAINT chk_assigned_with_type
            CHECK (loan_type = 'R' OR assigned_to IS NULL);
    END IF;
END $$;

-- Reseed assigned_to for recovery loans (only rows still NULL)
UPDATE loans SET assigned_to = 'Sarah Mitchell'
    WHERE loan_number IN ('2000007','2000008','2000009','2000023','2000024')
      AND assigned_to IS NULL;

UPDATE loans SET assigned_to = 'James Carter'
    WHERE loan_number IN ('2000027','2000029','2000036','2000056','2000058')
      AND assigned_to IS NULL;

UPDATE loans SET assigned_to = 'Rachel Torres'
    WHERE loan_number IN ('2000060','2000063','2000071','2000109','2000140')
      AND assigned_to IS NULL;

UPDATE loans SET assigned_to = 'David Nguyen'
    WHERE loan_number IN ('2000152','2000164','2000174','2000189','2000190')
      AND assigned_to IS NULL;
