

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
    public async Task Validate_StatusAndStructure(string json, bool shouldBeValid)
    {
        var result = _validator.Validate(json);

        await Assert.That(result.IsValid).IsEqualTo(shouldBeValid);
    }

    [Test]
    public async Task Validate_RejectsMalformedActionsAndArtifacts()
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

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains(e => e.Contains("actions[0].type") && e.Contains("required"));
        await Assert.That(result.Errors).Contains(e => e.Contains("actions[1]") && e.Contains("must be an object"));
        await Assert.That(result.Errors).Contains(e => e.Contains("artifacts[0]") && e.Contains("cannot be empty"));
    }

    [Test]
    public async Task Validate_EmitsWarningsForOptionalMetadata()
    {
        var json = """
        {
            "status":"succeeded",
            "unknownField":"ok"
        }
        """;

        var result = _validator.Validate(json);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Warnings).Contains("Unknown property 'unknownField' will be ignored");
        await Assert.That(result.Warnings).Contains("Optional property 'runId' is missing");
    }
}
