using AgentsDashboard.WorkerGateway.Adapters;

namespace AgentsDashboard.UnitTests.WorkerGateway.Adapters;

public class FailureClassificationTests
{
    [Test]
    public void Success_ReturnsSuccessfulClassification()
    {
        var classification = FailureClassification.Success();
        classification.Class.Should().Be(FailureClass.None);
        classification.Reason.Should().BeEmpty();
        classification.IsRetryable.Should().BeFalse();
    }

    [Test]
    public void FromClass_WithNoHints_CreatesClassification()
    {
        var classification = FailureClassification.FromClass(
            FailureClass.Timeout,
            "Operation timed out",
            true,
            30);

        classification.Class.Should().Be(FailureClass.Timeout);
        classification.Reason.Should().Be("Operation timed out");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(30);
        classification.RemediationHints.Should().BeEmpty();
    }

    [Test]
    public void FromClass_WithHints_CreatesClassificationWithHints()
    {
        var classification = FailureClassification.FromClass(
            FailureClass.RateLimitExceeded,
            "Rate limit hit",
            true,
            60,
            "Wait before retry",
            "Reduce frequency",
            "Contact support");

        classification.Class.Should().Be(FailureClass.RateLimitExceeded);
        classification.Reason.Should().Be("Rate limit hit");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(60);
        classification.RemediationHints.Should().HaveCount(3);
        classification.RemediationHints.Should().Contain("Wait before retry");
        classification.RemediationHints.Should().Contain("Reduce frequency");
        classification.RemediationHints.Should().Contain("Contact support");
    }

    [Test]
    [Arguments(FailureClass.None)]
    [Arguments(FailureClass.AuthenticationError)]
    [Arguments(FailureClass.RateLimitExceeded)]
    [Arguments(FailureClass.Timeout)]
    [Arguments(FailureClass.ResourceExhausted)]
    [Arguments(FailureClass.InvalidInput)]
    [Arguments(FailureClass.ConfigurationError)]
    [Arguments(FailureClass.NetworkError)]
    [Arguments(FailureClass.PermissionDenied)]
    [Arguments(FailureClass.NotFound)]
    [Arguments(FailureClass.InternalError)]
    [Arguments(FailureClass.Unknown)]
    public void FromClass_AllFailureClasses_AreValid(FailureClass failureClass)
    {
        var classification = FailureClassification.FromClass(failureClass, "Test reason", true, 10, "Hint");
        classification.Class.Should().Be(failureClass);
    }
}
