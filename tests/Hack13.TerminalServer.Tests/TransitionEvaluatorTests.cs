using Microsoft.Extensions.Logging.Abstractions;
using Hack13.Contracts.Protocol;
using Hack13.TerminalServer.Navigation;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Tests;

public class TransitionEvaluatorTests
{
    private static NavigationConfig CreateTestConfig()
    {
        return new NavigationConfig
        {
            InitialScreen = "sign_on",
            Credentials = new List<CredentialEntry>
            {
                new() { UserId = "TESTUSER", Password = "TEST1234" }
            },
            Transitions = new List<TransitionRule>
            {
                new()
                {
                    SourceScreen = "sign_on",
                    AidKey = "Enter",
                    Conditions = new() { ["user_id"] = "not_empty", ["password"] = "not_empty" },
                    TargetScreen = "loan_inquiry",
                    Validation = "credentials"
                },
                new()
                {
                    SourceScreen = "sign_on",
                    AidKey = "Enter",
                    Conditions = new(),
                    TargetScreen = "sign_on",
                    ErrorMessage = "User ID and password are required"
                },
                new()
                {
                    SourceScreen = "loan_inquiry",
                    AidKey = "Enter",
                    Conditions = new() { ["loan_number"] = "not_empty" },
                    TargetScreen = "loan_details",
                    Validation = "loan_exists"
                },
                new()
                {
                    SourceScreen = "loan_inquiry",
                    AidKey = "F3",
                    Conditions = new(),
                    TargetScreen = "sign_on"
                },
                new()
                {
                    SourceScreen = "loan_details",
                    AidKey = "F6",
                    Conditions = new(),
                    TargetScreen = "escrow_analysis"
                },
                new()
                {
                    SourceScreen = "loan_details",
                    AidKey = "F12",
                    Conditions = new(),
                    TargetScreen = "loan_inquiry"
                }
            }
        };
    }

    private static TestDataStore CreateTestData()
    {
        var store = new TestDataStore();
        // We can't easily call LoadFromFile without a file, so we'll test transitions
        // that don't require loan validation, or accept the "loan not found" result
        return store;
    }

    [Fact]
    public void Evaluate_ValidCredentials_TransitionsToLoanInquiry()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "sign_on" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_ENTER, new Dictionary<string, string>
        {
            ["user_id"] = "TESTUSER",
            ["password"] = "TEST1234"
        });

        Assert.True(result.Success);
        Assert.Equal("loan_inquiry", result.TargetScreen);
    }

    [Fact]
    public void Evaluate_InvalidCredentials_ReturnsError()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "sign_on" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_ENTER, new Dictionary<string, string>
        {
            ["user_id"] = "WRONG",
            ["password"] = "BAD"
        });

        Assert.False(result.Success);
        Assert.Equal("Invalid user ID or password", result.ErrorMessage);
    }

    [Fact]
    public void Evaluate_EmptyCredentials_ShowsRequiredMessage()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "sign_on" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_ENTER, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Equal("User ID and password are required", result.ErrorMessage);
    }

    [Fact]
    public void Evaluate_F3FromLoanInquiry_ReturnsToSignOn()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "loan_inquiry" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_F3, new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal("sign_on", result.TargetScreen);
    }

    [Fact]
    public void Evaluate_F6FromLoanDetails_GoesToEscrowAnalysis()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "loan_details" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_F6, new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal("escrow_analysis", result.TargetScreen);
    }

    [Fact]
    public void Evaluate_F12FromLoanDetails_GoesBack()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "loan_details" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_F12, new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal("loan_inquiry", result.TargetScreen);
    }

    [Fact]
    public void Evaluate_UnmappedKey_ReturnsInvalidKey()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "sign_on" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_F5, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("Invalid key", result.ErrorMessage);
    }

    [Fact]
    public void Evaluate_FieldValues_CopiedToDataUpdates()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "sign_on" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_ENTER, new Dictionary<string, string>
        {
            ["user_id"] = "TESTUSER",
            ["password"] = "TEST1234"
        });

        Assert.True(result.Success);
        Assert.Equal("TESTUSER", result.DataUpdates["user_id"]);
    }

    [Fact]
    public void Evaluate_DoesNotPersistPasswordInDataUpdates()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState { CurrentScreen = "sign_on" };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_ENTER, new Dictionary<string, string>
        {
            ["user_id"] = "TESTUSER",
            ["password"] = "TEST1234"
        });

        Assert.True(result.Success);
        Assert.DoesNotContain("password", result.DataUpdates.Keys);
    }

    [Fact]
    public void Evaluate_RequiredFieldDoesNotUseStaleSessionValue()
    {
        var config = CreateTestConfig();
        var evaluator = new TransitionEvaluator(config, CreateTestData(), NullLogger.Instance);
        var session = new SessionState
        {
            CurrentScreen = "sign_on",
            Data = new Dictionary<string, string>
            {
                ["user_id"] = "TESTUSER",
                ["password"] = "TEST1234"
            }
        };

        var result = evaluator.Evaluate(session, Tn5250Constants.AID_ENTER, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Equal("User ID and password are required", result.ErrorMessage);
    }
}
