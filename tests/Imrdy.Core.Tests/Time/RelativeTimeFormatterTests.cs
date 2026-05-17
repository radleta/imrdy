using FluentAssertions;
using Imrdy.Core.Time;

namespace Imrdy.Core.Tests.Time;

public class RelativeTimeFormatterTests
{
    [Fact]
    public void FormatDuration_ReturnsSeconds_WhenUnderOneMinute()
    {
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromSeconds(18))
            .Should().Be("18s");
    }

    [Fact]
    public void FormatDuration_ReturnsFractionalSecondsAsTruncated()
    {
        // 59.9s truncates to 59s (int cast)
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromSeconds(59.9))
            .Should().Be("59s");
    }

    [Fact]
    public void FormatDuration_ReturnsMinutes_WhenUnderOneHour()
    {
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromMinutes(2))
            .Should().Be("2m");
    }

    [Fact]
    public void FormatDuration_Returns59m_AtEdge()
    {
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromMinutes(59))
            .Should().Be("59m");
    }

    [Fact]
    public void FormatDuration_ReturnsHoursOnly_WhenNoRemainingMinutes()
    {
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromHours(3))
            .Should().Be("3h");
    }

    [Fact]
    public void FormatDuration_ReturnsHoursAndMinutes_WhenBothPresent()
    {
        // 5h 40m → "5h 40m"
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromMinutes(340))
            .Should().Be("5h 40m");
    }

    [Fact]
    public void FormatDuration_ReturnsHoursAndMinutes_At1h14m()
    {
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromMinutes(74))
            .Should().Be("1h 14m");
    }

    [Fact]
    public void FormatDuration_ReturnsDays_WhenAtLeast24Hours()
    {
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromDays(3))
            .Should().Be("3d");
    }

    [Fact]
    public void FormatDuration_ReturnsDays_At25Hours()
    {
        // 25h is 1d (int cast of TotalDays)
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromHours(25))
            .Should().Be("1d");
    }

    [Fact]
    public void FormatDuration_ReturnsZeroSeconds_ForZeroSpan()
    {
        RelativeTimeFormatter.FormatDuration(TimeSpan.Zero)
            .Should().Be("0s");
    }

    [Fact]
    public void FormatDuration_ReturnsZeroSeconds_ForSubSecond()
    {
        // 900ms truncates to 0s via int cast of TotalSeconds
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromMilliseconds(900))
            .Should().Be("0s");
    }

    [Fact]
    public void FormatDuration_ReturnsOneMinute_AtExactly60Seconds()
    {
        // TotalSeconds == 60 is NOT < 60, so it falls to the minutes branch → "1m"
        RelativeTimeFormatter.FormatDuration(TimeSpan.FromSeconds(60))
            .Should().Be("1m");
    }
}
