using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Features.Workspace.Services;
using AgentsDashboard.Contracts.Features.Workspace.Models.Domain;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceQuestionInboxTests
{
    [Test]
    public async Task SubmitRemainsDisabledUntilAllQuestionsAreAnsweredThenSendsAnswersAsync()
    {
        await using var context = WorkspaceBunitTestContext.Create();

        WorkspaceQuestionAnswersSubmissionRequest? submitted = null;

        var request = new RunQuestionRequestDocument
        {
            Id = "request-1",
            SourceToolName = "request_user_input",
            SourceSequence = 1,
            Questions =
            [
                new RunQuestionItemDocument
                {
                    Id = "q1",
                    Header = "First",
                    Prompt = "Choose one",
                    Order = 0,
                    Options =
                    [
                        new RunQuestionOptionDocument { Value = "yes", Label = "Yes", Description = "Proceed" },
                        new RunQuestionOptionDocument { Value = "no", Label = "No", Description = "Stop" }
                    ]
                },
                new RunQuestionItemDocument
                {
                    Id = "q2",
                    Header = "Second",
                    Prompt = "Choose again",
                    Order = 1,
                    Options =
                    [
                        new RunQuestionOptionDocument { Value = "a", Label = "A", Description = "Path A" },
                        new RunQuestionOptionDocument { Value = "b", Label = "B", Description = "Path B" }
                    ]
                }
            ]
        };

        var component = context.Render<WorkspaceQuestionInbox>(parameters => parameters
            .Add(p => p.QuestionRequests, new List<RunQuestionRequestDocument> { request })
            .Add(p => p.OnSubmit, (WorkspaceQuestionAnswersSubmissionRequest model) => submitted = model));

        var submitButton = component.Find("[data-testid='workspace-question-submit-request-1']");
        await Assert.That(submitButton.HasAttribute("disabled")).IsTrue();

        component.Find("[data-testid='workspace-question-option-request-1-q1-yes']").Click();
        component.Find("[data-testid='workspace-question-option-request-1-q2-a']").Click();

        submitButton = component.Find("[data-testid='workspace-question-submit-request-1']");
        await Assert.That(submitButton.HasAttribute("disabled")).IsFalse();

        submitButton.Click();

        await Assert.That(submitted is not null).IsTrue();
        if (submitted is null)
        {
            return;
        }

        await Assert.That(submitted.QuestionRequestId).IsEqualTo("request-1");
        await Assert.That(submitted.Answers.Count).IsEqualTo(2);

        var firstAnswer = submitted.Answers.FirstOrDefault(answer => answer.QuestionId == "q1");
        var secondAnswer = submitted.Answers.FirstOrDefault(answer => answer.QuestionId == "q2");

        await Assert.That(firstAnswer is not null).IsTrue();
        await Assert.That(secondAnswer is not null).IsTrue();

        if (firstAnswer is null || secondAnswer is null)
        {
            return;
        }

        await Assert.That(firstAnswer.SelectedOptionValue).IsEqualTo("yes");
        await Assert.That(secondAnswer.SelectedOptionValue).IsEqualTo("a");
    }
}
