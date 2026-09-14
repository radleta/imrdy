using FluentAssertions;
using Imrdy.Core.Publishing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Imrdy.Core.Tests.Publishing;

/// <summary>
/// The caller-level cover for <c>f-filesink-no-socket</c>: a file-sink publisher opens no
/// socket, so nothing it does ever reaches <c>WireListener.Health()</c> and a surface that
/// enumerates publishers from the listener alone reports it as absent while it is actively
/// delivering. These tests start at the <em>disk</em> — a beat written by the real
/// <see cref="HeartbeatWriter"/>, or the literal bytes an older one wrote — and never hand
/// <c>ConnectionsViewModelBuilder</c> its input directly, which is the fixture shape that missed
/// the defect.
/// </summary>
public class HeartbeatMachinesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A machine name the filename token flattens: a dot and mixed case.</summary>
    private const string Machine = "PC-Excalibur-Ubuntu-24.04";

    private readonly string _root;
    private readonly string _sessions;

    public HeartbeatMachinesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "imrdy-heartbeat-machines", Guid.NewGuid().ToString());
        _sessions = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(_sessions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A publisher registered against this receiver's sessions directory, beating once.</summary>
    private void Beat(string machine, DateTimeOffset at) =>
        new HeartbeatWriter(
                () => [new PublisherEntry { Name = "receiver", Endpoint = _sessions }],
                () => machine,
                NullLogger.Instance)
            .WriteIfDue(at).Should().BeTrue();

    /// <summary>What a publisher built before the name was carried wrote: the timestamp alone.</summary>
    private void OldBeat(string machine, DateTimeOffset at)
    {
        Directory.CreateDirectory(PublisherHeartbeat.DirectoryFor(_sessions));
        File.WriteAllText(PublisherHeartbeat.PathFor(_sessions, machine), at.ToString("O"));
    }

    private static ConnectionsViewModel Links(PublisherConfig publishers, IReadOnlyList<MachineBeat> beats) =>
        LinksReport.Build(publishers, new NetworkConfig(), "receiver-box", wslDistro: null, beats, Now);

    [Fact]
    public void Read_NamesThePublisherFromItsOwnBeat_WithNoSessionOnThisMachine()
    {
        // The state the lossy filename could not survive: right after a retire, the beat is
        // present and every session that could have carried origin_machine is gone.
        Beat(Machine, Now.AddSeconds(-3));

        Directory.GetFiles(_sessions).Should().BeEmpty();
        HeartbeatMachines.Read(_sessions).Should().ContainSingle().Which.Name.Should().Be(
            Machine,
            "the beat carries the publisher's own name, so the flattened filename never has to be "
            + "reversed into one");
    }

    [Fact]
    public void LinksReport_WithNoPublishersJsonAtAll_StillReportsADeliveringFileSinkPublisher()
    {
        // The shape the user's demo measured: a fresh beat on disk and an empty publishers.json,
        // which printed "No links registered." while the publisher was delivering.
        Beat(Machine, Now.AddSeconds(-3));

        var row = Links(new PublisherConfig(), HeartbeatMachines.Read(_sessions)).Rows.Should().ContainSingle().Subject;

        row.Name.Should().Be(Machine);
        row.IsRegistered.Should().BeFalse("a receiver holds no allow-list (D24)");
        row.Endpoint.Should().BeNull();
        row.Inbound!.State.Should().Be(SinkState.FileSink, "there is no connection to call connected (D27)");
        row.LastDelivery.Should().Be("3s ago");
        ConnectionRowFormatter.LastError(row).Should().BeEmpty();
        row.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void LinksReport_RegisteredMachineWithABeat_ProducesOneRowThatKeepsItsRecord()
    {
        Beat(Machine, Now.AddSeconds(-3));

        var registered = new PublisherConfig
        {
            Publishers = [new PublisherEntry { Name = Machine, Endpoint = null, DesktopIndex = 3, Muted = true }],
        };

        var row = Links(registered, HeartbeatMachines.Read(_sessions)).Rows.Should().ContainSingle(
            "a machine known by both a record and a beat is one publisher, not two").Subject;

        row.IsRegistered.Should().BeTrue();
        row.DesktopIndex.Should().Be(3, "the record's own values must survive the beat");
        row.Muted.Should().BeTrue();
        row.Inbound!.State.Should().Be(SinkState.FileSink);
    }

    [Fact]
    public void LinksReport_StaleBeat_StaysFileSinkAndSaysSo()
    {
        Beat(Machine, Now - PublisherHeartbeat.StaleAfter - TimeSpan.FromMinutes(1));

        var row = Links(new PublisherConfig(), HeartbeatMachines.Read(_sessions)).Rows.Should().ContainSingle().Subject;

        row.Name.Should().Be(Machine);
        row.Inbound!.State.Should().Be(SinkState.FileSink, "imrdy links must not exit 1 on a distro that is merely down");
        ConnectionRowFormatter.LastError(row).Should().Contain("no heartbeat for");
    }

    [Fact]
    public void Read_WithNoBeatsAtAll_ReportsNothing()
    {
        HeartbeatMachines.Read(_sessions).Should().BeEmpty(
            "absence is not disconnection, and a receiver no file sink has ever written to must "
            + "behave exactly as it did before the heartbeat existed");
    }

    [Fact]
    public void Read_TimestampOnlyBeatFromAnOlderPublisher_StillReportsIt_UnderItsToken()
    {
        // A partial upgrade: this receiver reads names, that publisher does not write one yet.
        OldBeat(Machine, Now.AddSeconds(-3));

        var beat = HeartbeatMachines.Read(_sessions).Should().ContainSingle().Subject;

        beat.Name.Should().Be(PublisherHeartbeat.TokenFor(Machine), "the token is the only name that beat has");
        beat.BeatAt.Should().Be(Now.AddSeconds(-3));
    }

    [Fact]
    public void LinksReport_OnlyATimestampOnlyBeatsRow_IsMarkedTokenNamed()
    {
        // r-11: the window leaves Add…'s name empty on exactly this row, so the flag must come
        // from the old beat on disk and from nowhere else.
        OldBeat("old-box.lan", Now.AddSeconds(-3));
        Beat(Machine, Now.AddSeconds(-3));

        var rows = Links(new PublisherConfig(), HeartbeatMachines.Read(_sessions)).Rows;

        rows.Should().HaveCount(2);
        rows.Single(r => r.Name == PublisherHeartbeat.TokenFor("old-box.lan")).NameIsToken.Should().BeTrue();
        rows.Single(r => r.Name == Machine).NameIsToken.Should().BeFalse("its name came from inside the beat");
    }

    [Fact]
    public void LinksReport_TimestampOnlyBeat_StillJoinsItsRegisteredRecord()
    {
        OldBeat(Machine, Now.AddSeconds(-3));

        var registered = new PublisherConfig
        {
            Publishers = [new PublisherEntry { Name = Machine, Endpoint = null, DesktopIndex = 2 }],
        };

        var row = Links(registered, HeartbeatMachines.Read(_sessions)).Rows.Should().ContainSingle(
            "a record claims its beat by token, which an older beat still has").Subject;

        row.Name.Should().Be(Machine);
        row.IsRegistered.Should().BeTrue();
        row.Inbound!.State.Should().Be(SinkState.FileSink);
    }

    [Fact]
    public void Read_ControlCharactersInABeatsName_AreEscapedRatherThanRendered()
    {
        // The name arrived off another machine's mount, so it is untrusted however it was written.
        Directory.CreateDirectory(PublisherHeartbeat.DirectoryFor(_sessions));
        File.WriteAllText(
            PublisherHeartbeat.PathFor(_sessions, "evil"),
            "evil[2Jbox\n" + Now.ToString("O"));

        var name = HeartbeatMachines.Read(_sessions).Should().ContainSingle().Subject.Name;

        name.Should().Be("evil\\x1b[2Jbox");
    }
}
