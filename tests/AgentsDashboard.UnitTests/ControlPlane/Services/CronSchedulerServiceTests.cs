using AgentsDashboard.Contracts.Domain;
using Cronos;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class CronSchedulerServiceTests
{
    [Test]
    [Arguments("* * * * *", true)]
    [Arguments("0 * * * *", true)]
    [Arguments("0 0 * * *", true)]
    [Arguments("0 0 1 * *", true)]
    [Arguments("0 0 1 1 *", true)]
    [Arguments("0 0 * * 0", true)]
    [Arguments("invalid", false)]
    [Arguments("", false)]
    public void CronExpression_ParseValidatesCorrectly(string expression, bool isValid)
    {
        if (!isValid)
        {
            var action = () => CronExpression.Parse(expression, CronFormat.Standard);
            action.Should().Throw<Exception>();
            return;
        }

        var cron = CronExpression.Parse(expression, CronFormat.Standard);
        cron.Should().NotBeNull();
    }

    [Test]
    public void CronExpression_EveryMinute_ReturnsNextOccurrence()
    {
        var cron = CronExpression.Parse("* * * * *", CronFormat.Standard);
        var now = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2024, 1, 15, 10, 31, 0, DateTimeKind.Utc));
    }

    [Test]
    public void CronExpression_Hourly_ReturnsNextOccurrence()
    {
        var cron = CronExpression.Parse("0 * * * *", CronFormat.Standard);
        var now = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2024, 1, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public void CronExpression_DailyAtMidnight_ReturnsNextOccurrence()
    {
        var cron = CronExpression.Parse("0 0 * * *", CronFormat.Standard);
        var now = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2024, 1, 16, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public void ComputeNextRun_OneShot_ReturnsNow()
    {
        var task = new TaskDocument
        {
            Kind = TaskKind.OneShot,
            Enabled = true,
        };

        var now = DateTime.UtcNow;
        var result = ComputeNextRun(task, now);

        result.Should().Be(now);
    }

    [Test]
    public void ComputeNextRun_DisabledTask_ReturnsNull()
    {
        var task = new TaskDocument
        {
            Kind = TaskKind.Cron,
            Enabled = false,
            CronExpression = "* * * * *",
        };

        var result = ComputeNextRun(task, DateTime.UtcNow);

        result.Should().BeNull();
    }

    [Test]
    public void ComputeNextRun_CronWithoutExpression_ReturnsNull()
    {
        var task = new TaskDocument
        {
            Kind = TaskKind.Cron,
            Enabled = true,
            CronExpression = "",
        };

        var result = ComputeNextRun(task, DateTime.UtcNow);

        result.Should().BeNull();
    }

    [Test]
    public void ComputeNextRun_EventDriven_ReturnsNull()
    {
        var task = new TaskDocument
        {
            Kind = TaskKind.EventDriven,
            Enabled = true,
        };

        var result = ComputeNextRun(task, DateTime.UtcNow);

        result.Should().BeNull();
    }

    [Test]
    public void ComputeNextRun_ValidCron_ReturnsNextOccurrence()
    {
        var task = new TaskDocument
        {
            Kind = TaskKind.Cron,
            Enabled = true,
            CronExpression = "0 * * * *",
        };

        var now = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var result = ComputeNextRun(task, now);

        result.Should().NotBeNull();
        result.Should().Be(new DateTime(2024, 1, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    [Arguments("*/5 * * * *", "2024-01-15T10:00:00Z", "2024-01-15T10:05:00Z")]
    [Arguments("0 */2 * * *", "2024-01-15T10:00:00Z", "2024-01-15T12:00:00Z")]
    [Arguments("30 9 * * 1-5", "2024-01-15T10:00:00Z", "2024-01-16T09:30:00Z")]
    public void ComputeNextRun_VariousCronExpressions(
        string cronExpression,
        string nowStr,
        string expectedStr)
    {
        var task = new TaskDocument
        {
            Kind = TaskKind.Cron,
            Enabled = true,
            CronExpression = cronExpression,
        };

        var now = DateTime.Parse(nowStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var expected = DateTime.Parse(expectedStr, null, System.Globalization.DateTimeStyles.RoundtripKind);

        var result = ComputeNextRun(task, now);

        result.Should().Be(expected);
    }

    private static DateTime? ComputeNextRun(TaskDocument task, DateTime nowUtc)
    {
        if (!task.Enabled)
            return null;

        if (task.Kind == TaskKind.OneShot)
            return nowUtc;

        if (task.Kind != TaskKind.Cron || string.IsNullOrWhiteSpace(task.CronExpression))
            return null;

        var expression = CronExpression.Parse(task.CronExpression, CronFormat.Standard);
        return expression.GetNextOccurrence(nowUtc, TimeZoneInfo.Utc);
    }
}
