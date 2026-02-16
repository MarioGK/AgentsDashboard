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

        result.IsValid.Should().Be(shouldBeValid);
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

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("actions[0].type") && e.Contains("required"));
        result.Errors.Should().Contain(e => e.Contains("actions[1]") && e.Contains("must be an object"));
        result.Errors.Should().Contain(e => e.Contains("artifacts[0]") && e.Contains("cannot be empty"));
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

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain("Unknown property 'unknownField' will be ignored");
        result.Warnings.Should().Contain("Optional property 'runId' is missing");
    }
}
