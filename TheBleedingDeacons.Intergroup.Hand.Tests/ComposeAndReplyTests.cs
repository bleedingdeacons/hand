using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The handset as a sender: replying to a message, and putting a job
/// back out.
///
/// <para>The two look alike and behave oppositely, which is the whole of
/// what is worth asserting here. A reply says something about an alert
/// and changes nothing: the responder still has the job, the card stays,
/// the alarm arrangement is untouched. A pass-back gives the job away,
/// so the card goes and the history says so.</para>
/// </summary>
public sealed class ComposeAndReplyTests
{
	private readonly FakeReachClient _reach = new();
	private readonly FakeConfigurationService _config = new() { DeviceToken = "abc123" };
	private readonly FakeAlarm _alarm = new();
	private readonly FakePresenter _presenter = new();
	private readonly InlineDispatcher _dispatcher = new();
	private readonly InMemoryAlertHistoryStore _historyStore = new();

	private AlertHistory History => field ??= new AlertHistory(_historyStore, _dispatcher);

	private AlertService Build() => new(_reach, _config, _alarm, _presenter, _dispatcher, History);

	// ── Replying ──────────────────────────────────────────────────────

	[Fact]
	public async Task ReplyAsync_SendsTheTextToReach()
	{
		using var service = Build();

		var sent = await service.ReplyAsync(7, "  On my way  ");

		Assert.True(sent);
		Assert.Single(_reach.Replies);
		Assert.Equal(7, _reach.Replies[0].AlertId);

		// Trimmed on the way out. A reply that is one stray space is not a
		// reply, and the server caps and strips the rest.
		Assert.Equal("On my way", _reach.Replies[0].Body);
	}

	/// <summary>
	/// <b>Replying settles nothing.</b> It is not a second person taking
	/// the job on, so an outstanding alert stays outstanding and its
	/// button goes on saying Acknowledge. Getting this wrong would silence
	/// a live alert because somebody typed a sentence about it.
	/// </summary>
	[Fact]
	public async Task ReplyAsync_LeavesTheAlertOutstanding()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		await service.ReplyAsync(7, "Looking into it");

		Assert.Single(service.Active);
		Assert.False(service.Active[0].AcknowledgedHere);
		Assert.Empty(_reach.Acknowledged);
	}

	/// <summary>
	/// The case the whole reply path exists for. When somebody else
	/// answers, Reach stops serving the message and Hand removes every
	/// local copy — so a reply has to work from an id alone, with no alert
	/// in hand. That is why the service takes an id rather than a
	/// <c>HandAlert</c>.
	/// </summary>
	[Fact]
	public async Task ReplyAsync_WorksAfterSomebodyElseAnswered()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: "m-1"));

		// The notice Reach raises when another responder acknowledges. It
		// removes every card sharing the uuid.
		await service.HandlePushAsync(Notice("m-1"));

		Assert.DoesNotContain(service.Active, a => a.Id == 7);

		var sent = await service.ReplyAsync(7, "I could have taken that");

		Assert.True(sent);
		Assert.Single(_reach.Replies);
	}

	[Fact]
	public async Task ReplyAsync_RefusesAnEmptyBodyWithoutCallingReach()
	{
		using var service = Build();

		Assert.False(await service.ReplyAsync(7, "   "));
		Assert.Empty(_reach.Replies);
	}

	[Fact]
	public async Task ReplyAsync_ReportsARefusal()
	{
		_reach.ReplyResult = ReachResult<bool>.Fail(ReachFailure.Network, "offline");
		using var service = Build();

		Assert.False(await service.ReplyAsync(7, "Hello"));
	}

	[Fact]
	public async Task ReplyAsync_DoesNothingWithoutAToken()
	{
		_config.DeviceToken = string.Empty;
		using var service = Build();

		Assert.False(await service.ReplyAsync(7, "Hello"));
		Assert.Empty(_reach.Replies);
	}

	// ── Passing a job back ────────────────────────────────────────────

	[Fact]
	public async Task ResendAsync_RemovesTheCardAndRecordsItAsPassedOn()
	{
		using var service = Build();
		await History.LoadAsync();
		await service.HandlePushAsync(Alerts.New(7));
		await service.AcknowledgeAsync(service.Active[0]);

		var sent = await service.ResendAsync(service.Active[0]);

		Assert.True(sent);
		Assert.Equal([7], _reach.Resent);

		// The job is no longer this responder's, so the card goes — unlike
		// a reply, which leaves everything where it was.
		Assert.Empty(service.Active);

		var entry = Assert.Single(History.Entries, e => e.Id == 7);
		Assert.Equal(AlertHistoryStatus.PassedOn, entry.Status);
		Assert.Equal("Passed back to the rota", entry.StatusLine);
	}

	/// <summary>
	/// A refused pass-back must leave the alert exactly where it was. The
	/// responder still has the job — telling them otherwise would take a
	/// live callback off their screen on the strength of a request that
	/// never landed.
	/// </summary>
	[Fact]
	public async Task ResendAsync_KeepsTheCardWhenReachRefuses()
	{
		_reach.ResendResult = ReachResult<bool>.Fail(ReachFailure.Network, "offline");
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		var sent = await service.ResendAsync(service.Active[0]);

		Assert.False(sent);
		Assert.Single(service.Active);
	}

	[Fact]
	public async Task ResendAsync_DoesNothingWithoutAToken()
	{
		_config.DeviceToken = string.Empty;
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		Assert.False(await service.ResendAsync(service.Active[0]));
		Assert.Empty(_reach.Resent);
	}

	[Fact]
	public async Task ResendAsync_RejectsNull()
	{
		using var service = Build();

		await Assert.ThrowsAsync<ArgumentNullException>(() => service.ResendAsync(null!));
	}

	/// <summary>
	/// The narrow half of the transition this feature opened up. Passing
	/// on may overwrite an acknowledgement, because it can only follow
	/// one — but nothing else may overwrite anything, or a late notice
	/// would go on rewriting settled rows.
	/// </summary>
	[Fact]
	public async Task PassingOnDoesNotOverwriteAJobSomebodyElseAnswered()
	{
		using var service = Build();
		await History.LoadAsync();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: "m-1"));
		await service.HandlePushAsync(Notice("m-1"));

		var entry = Assert.Single(History.Entries, e => e.Id == 7);
		Assert.Equal(AlertHistoryStatus.Answered, entry.Status);

		// A stray pass-back against a job this handset never took must
		// leave the record alone.
		await History.SettleAsync(7, AlertHistoryStatus.PassedOn, 123);

		Assert.Equal(AlertHistoryStatus.Answered, entry.Status);
		Assert.Equal("Answered by Sam T.", entry.StatusLine);
	}

	// ── History, as the reply button reads it ─────────────────────────

	/// <summary>
	/// An alert somebody else answered can still be replied to. This is
	/// the property the history page's Reply button binds, and the answer
	/// has to be yes for exactly the entries whose cards are gone.
	/// </summary>
	[Fact]
	public void AnAnsweredEntryCanStillBeRepliedTo()
	{
		var entry = new AlertHistoryEntry { Status = AlertHistoryStatus.Answered };

		Assert.True(entry.CanReply);
	}

	[Fact]
	public void AnExpiredEntryCannotBeRepliedTo()
	{
		// Reach purges an alert a day after its window shuts, so this
		// would answer 404. A button that cannot work is worse than none.
		var entry = new AlertHistoryEntry { Status = AlertHistoryStatus.Expired };

		Assert.False(entry.CanReply);
	}

	/// <summary>
	/// A subject-only alert has to be openable, because the Reply button
	/// lives in the expanded half of the row. Plenty of alerts are only a
	/// subject line, and the one somebody else answered first is exactly
	/// the one this button exists for.
	/// </summary>
	[Fact]
	public void ASubjectOnlyEntryStillOpens()
	{
		var entry = new AlertHistoryEntry { Subject = "Callback wanted" };

		Assert.True(entry.HasDetail);
	}

	[Fact]
	public void AnExpiredSubjectOnlyEntryDoesNotOpen()
	{
		var entry = new AlertHistoryEntry
		{
			Subject = "Callback wanted",
			Status = AlertHistoryStatus.Expired,
		};

		Assert.False(entry.HasDetail);
	}

	// ── Feature discovery ─────────────────────────────────────────────

	/// <summary>
	/// <b>The important half of can_send is the default.</b> Hand updates
	/// itself and the site it talks to does not, so a handset newer than
	/// its Reach is the ordinary case — and that server says nothing here.
	/// Reading silence as "yes" would put a compose button on screen that
	/// answers 404.
	/// </summary>
	[Fact]
	public void ASessionFromAnOlderServerCannotSend()
	{
		Assert.False(new DeviceSession().CanSend);
	}

	// ── The directory, as the picker shows it ─────────────────────────

	[Fact]
	public void AMemberReadsAsNameAndHomeGroup()
	{
		var member = new HandMember
		{
			AnonymousName = "Alice K.",
			HomeGroup = "Tuesday Bristol",
			Reachable = true,
		};

		// A rota has more than one Dave; the home group is what tells them
		// apart, and it is why the picker shows both.
		Assert.Equal("Alice K. — Tuesday Bristol", member.Display);
		Assert.Equal(string.Empty, member.ReachabilityNote);
	}

	[Fact]
	public void AMemberWithNoHomeGroupIsJustTheName()
	{
		var member = new HandMember { AnonymousName = "Alice K." };

		Assert.Equal("Alice K.", member.Display);
	}

	[Fact]
	public void AnUnreachableMemberSaysSoRatherThanBeingHidden()
	{
		var member = new HandMember { AnonymousName = "Pat R." };

		Assert.False(member.Reachable);
		Assert.Equal("No handset enrolled", member.ReachabilityNote);
	}

	[Fact]
	public void ACommitteeCountsTheBranchAndIndentsByDepth()
	{
		var committee = new HandCommittee { Name = "Health", Depth = 1, Handsets = 3 };

		Assert.Equal("3 handsets", committee.HandsetsLine);
		Assert.Equal(16, committee.Indent);
		Assert.True(committee.Reachable);
	}

	[Fact]
	public void ACommitteeWithOneHandsetSaysItInTheSingular()
	{
		Assert.Equal("1 handset", new HandCommittee { Handsets = 1 }.HandsetsLine);
	}

	[Fact]
	public void ACommitteeNobodyIsOnIsNotReachable()
	{
		Assert.False(new HandCommittee { Name = "Archives" }.Reachable);
	}

	/// <summary>
	/// The notice Reach raises when another responder acknowledges: blue
	/// and informational, so Hand treats it quietly by the fields rather
	/// than by its kind.
	/// </summary>
	private static HandAlert Notice(string acknowledges)
	{
		var notice = Alerts.New(
			id: 999,
			kind: HandAlert.KindMessageAcknowledged,
			level: HandAlert.LevelBlue,
			response: HandAlert.ResponseNone);

		notice.Payload["ack_message_uuid"] = acknowledges;
		notice.Payload["ack_responder"] = "Sam T.";

		return notice;
	}
}
