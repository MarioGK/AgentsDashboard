using AgentsDashboard.Contracts.Validation;

namespace AgentsDashboard.UnitTests.Contracts.Validation;

public class HarnessEnvelopeValidatorTests
{
    private static readonly HarnessEnvelopeValidator _validator = new();

    [Test]
    [Arguments("""{"status":"SUCCEEDED","summary":"done"}""", true)]
    [Arguments("""{"status":"failed"}""", true)]
    [Arguments("""{"status":"unknown"}""", true)]
    [Arguments("""{"status":""}""", false)]
    [Arguments("""{"status":123}""", false)]
    [Arguments("""{}""", false)]
    [Arguments("", false)]
    public void Validate_StatusAndStructure(string json, bool shouldBeValid)
    {
        var result = _validator.Validate(json);

        Assert.That(result.IsValid).IsEqualTo(shouldBeValid);
    }

    [Test]
    public void Validate_RejectsMalformedActionsAndArtifacts()
    {
        var json = """
        {
            "status": "succeeded",
            "actions": [
                {"description":"missing type"},
                "oops"
            ],
            "artifacts": [""]
        }
        """;

        var result = _validator.Validate(json);

        Assert.That(result.IsValid).IsFalse();
        Assert.That(result.Errors).Contains(e => e.Contains("actions[0].type") && e.Contains("required"));
        Assert.That(result.Errors).Contains(e => e.Contains("actions[1]") && e.Contains("must be an object"));
        Assert.That(result.Errors).Contains(e => e.Contains("artifacts[0]") && e.Contains("cannot be empty"));
    }

    [Test]
    public void Validate_EmitsWarningsForOptionalMetadata()
    {
        var json = """
        {
            "status":"succeeded",
            "unknownField":"ok"
        }
        """;

        var result = _validator.Validate(json);

        Assert.That(result.IsValid).IsTrue();
        Assert.That(result.Warnings).Contains("Unknown property 'unknownField' will be ignored");
        Assert.That(result.Warnings).Contains("Optional property 'runId' is missing");
    }
}
