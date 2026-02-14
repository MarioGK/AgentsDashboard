using AgentsDashboard.Contracts.Validation;

namespace AgentsDashboard.UnitTests.Contracts.Validation;

public class HarnessEnvelopeValidatorTests
{
    private readonly HarnessEnvelopeValidator _validator = new();

    [Fact]
    public void Validate_ReturnsSuccess_ForValidMinimalEnvelope()
    {
        var json = """{"status":"succeeded"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsSuccess_ForFullyPopulatedEnvelope()
    {
        var json = """
        {
            "runId": "run-123",
            "taskId": "task-456",
            "status": "succeeded",
            "summary": "Task completed successfully",
            "error": "",
            "actions": [
                {"type": "file_write", "description": "Created output.txt", "target": "/workspace/output.txt"}
            ],
            "artifacts": ["/workspace/output.txt", "/workspace/result.json"],
            "metrics": {"durationMs": 1500.5, "tokensUsed": 1024},
            "metadata": {"exitCode": "0", "stdout": "done"},
            "rawOutputRef": "/storage/runs/run-123/raw.log"
        }
        """;

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    [InlineData("unknown")]
    [InlineData("cancelled")]
    [InlineData("pending")]
    public void Validate_AcceptsValidStatusValues(string status)
    {
        var json = $"{{\"status\":\"{status}\"}}";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsError_ForMissingStatus()
    {
        var json = """{"runId":"run-123"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'status'"));
    }

    [Fact]
    public void Validate_ReturnsError_ForInvalidStatus()
    {
        var json = """{"status":"invalid_status"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("status") && e.Contains("succeeded"));
    }

    [Fact]
    public void Validate_ReturnsError_ForEmptyStatus()
    {
        var json = """{"status":""}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'status'") && e.Contains("empty"));
    }

    [Fact]
    public void Validate_ReturnsError_ForNonStringStatus()
    {
        var json = """{"status":123}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'status'") && e.Contains("string"));
    }

    [Fact]
    public void Validate_ReturnsError_ForEmptyJson()
    {
        var result = _validator.Validate("");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_ReturnsError_ForNullJson()
    {
        var result = _validator.Validate(null!);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_ReturnsError_ForWhitespaceJson()
    {
        var result = _validator.Validate("   ");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_ReturnsError_ForInvalidJson()
    {
        var result = _validator.Validate("{invalid json}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid JSON"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenRootIsNotObject()
    {
        var json = """["status","succeeded"]""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("object"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenActionsIsNotArray()
    {
        var json = """{"status":"succeeded","actions":"not-array"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'actions'") && e.Contains("array"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenActionItemIsNotObject()
    {
        var json = """{"status":"succeeded","actions":["string"]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("actions[0]") && e.Contains("object"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenActionMissingType()
    {
        var json = """{"status":"succeeded","actions":[{"description":"test"}]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("actions[0].type") && e.Contains("required"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenActionTypeIsEmpty()
    {
        var json = """{"status":"succeeded","actions":[{"type":""}]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("actions[0].type") && e.Contains("non-empty"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenActionHasUnknownProperty()
    {
        var json = """{"status":"succeeded","actions":[{"type":"test","unknown":"value"}]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("actions[0]") && e.Contains("unknown property"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenArtifactsIsNotArray()
    {
        var json = """{"status":"succeeded","artifacts":"not-array"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'artifacts'") && e.Contains("array"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenArtifactItemIsNotString()
    {
        var json = """{"status":"succeeded","artifacts":[123]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("artifacts[0]") && e.Contains("string"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenArtifactIsEmpty()
    {
        var json = """{"status":"succeeded","artifacts":[""]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("artifacts[0]") && e.Contains("empty"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenMetricsIsNotObject()
    {
        var json = """{"status":"succeeded","metrics":"not-object"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'metrics'") && e.Contains("object"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenMetricValueIsNotNumber()
    {
        var json = """{"status":"succeeded","metrics":{"duration":"not-number"}}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("metrics['duration']") && e.Contains("number"));
    }

    [Fact]
    public void Validate_AcceptsNumericMetricValues()
    {
        var json = """{"status":"succeeded","metrics":{"int":100,"float":1.5,"negative":-10}}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsError_WhenMetadataIsNotObject()
    {
        var json = """{"status":"succeeded","metadata":"not-object"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("'metadata'") && e.Contains("object"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenMetadataValueIsNotString()
    {
        var json = """{"status":"succeeded","metadata":{"key":123}}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("metadata['key']") && e.Contains("string"));
    }

    [Fact]
    public void Validate_ReturnsWarning_ForMissingRunId()
    {
        var json = """{"status":"succeeded"}""";

        var result = _validator.Validate(json);

        result.Warnings.Should().Contain(w => w.Contains("'runId'"));
    }

    [Fact]
    public void Validate_ReturnsWarning_ForMissingTaskId()
    {
        var json = """{"status":"succeeded"}""";

        var result = _validator.Validate(json);

        result.Warnings.Should().Contain(w => w.Contains("'taskId'"));
    }

    [Fact]
    public void Validate_ReturnsWarning_ForUnknownProperties()
    {
        var json = """{"status":"succeeded","unknownField":"value"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("unknownField"));
    }

    [Fact]
    public void Validate_HandlesMultipleErrors()
    {
        var json = """{"status":"invalid","artifacts":[123],"metrics":{"a":"b"}}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Validate_AcceptsStatusCaseInsensitively()
    {
        var json = """{"status":"SUCCEEDED"}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsEmptyActionsArray()
    {
        var json = """{"status":"succeeded","actions":[]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsEmptyArtifactsArray()
    {
        var json = """{"status":"succeeded","artifacts":[]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsEmptyMetricsObject()
    {
        var json = """{"status":"succeeded","metrics":{}}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsEmptyMetadataObject()
    {
        var json = """{"status":"succeeded","metadata":{}}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsActionWithOnlyType()
    {
        var json = """{"status":"succeeded","actions":[{"type":"test"}]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsError_WhenActionDescriptionIsNotString()
    {
        var json = """{"status":"succeeded","actions":[{"type":"test","description":123}]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("actions[0].description") && e.Contains("string"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenActionTargetIsNotString()
    {
        var json = """{"status":"succeeded","actions":[{"type":"test","target":123}]}""";

        var result = _validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("actions[0].target") && e.Contains("string"));
    }

    [Fact]
    public void Validate_ReturnsWarning_WhenRawOutputRefIsNotString()
    {
        var json = """{"status":"succeeded","rawOutputRef":123}""";

        var result = _validator.Validate(json);

        result.Warnings.Should().Contain(w => w.Contains("'rawOutputRef'") && w.Contains("string"));
    }

    [Fact]
    public void Validate_ReturnsWarning_WhenRunIdIsNotString()
    {
        var json = """{"status":"succeeded","runId":123}""";

        var result = _validator.Validate(json);

        result.Warnings.Should().Contain(w => w.Contains("'runId'") && w.Contains("string"));
    }

    [Fact]
    public void Validate_ReturnsWarning_WhenTaskIdIsNotString()
    {
        var json = """{"status":"succeeded","taskId":123}""";

        var result = _validator.Validate(json);

        result.Warnings.Should().Contain(w => w.Contains("'taskId'") && w.Contains("string"));
    }
}
