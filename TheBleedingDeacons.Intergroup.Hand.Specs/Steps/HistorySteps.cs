using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>
/// What the handset remembers afterwards — the answer to "what came in
/// last night, and what became of it".
/// </summary>
[Binding]
public sealed class HistorySteps(World world)
{
	[Given(@"^the history has been loaded$")]
	public async Task LoadHistory() => await world.History.LoadAsync();

	/// <summary>
	/// Straight into the history rather than through the alert loop. The
	/// cap is a floor under a promise about storage, not behaviour of the
	/// loop, and admitting five hundred alerts to assert on it would say
	/// nothing extra and take a hundred times as long.
	/// </summary>
	[Given(@"^(\d+) alerts have already been remembered$")]
	public async Task ManyRemembered(int count)
	{
		for (var id = 1; id <= count; id++)
		{
			await world.History.RecordAsync(world.Alert(id), World.Now);
		}
	}

	[Then(@"^alert (\d+)'s history row is closed$")]
	public void RowIsClosed(long id) =>
		world.Remembered(id).ShouldNotBeNull().IsExpanded.ShouldBeFalse();

	[When(@"^the app is killed and opened again$")]
	public async Task Restart() => await world.Restart().LoadAsync();

	[When(@"^the history is cleared$")]
	public async Task Clear() => await world.History.ClearAsync();

	[Then(@"^the history remembers alert (\d+) as (outstanding|acknowledged|answered|closed|expired|passed back)$")]
	public void RemembersAs(long id, string outcome) =>
		world.Remembered(id).ShouldNotBeNull().Status.ShouldBe(Status(outcome));

	[Then(@"^the history remembers nothing about alert (\d+)$")]
	public void RemembersNothing(long id) => world.Remembered(id).ShouldBeNull();

	[Then(@"^the history says alert (\d+) was answered by ""(.+)""$")]
	public void AnsweredBy(long id, string who)
	{
		var entry = world.Remembered(id).ShouldNotBeNull();

		entry.Status.ShouldBe(AlertHistoryStatus.Answered);
		entry.AnsweredBy.ShouldBe(who);
		entry.StatusLine.ShouldBe($"Answered by {who}");
	}

	[Then(@"^alert (\d+)'s history row reads ""(.+)""$")]
	public void RowReads(long id, string line) =>
		world.Remembered(id).ShouldNotBeNull().StatusLine.ShouldBe(line);

	[Then(@"^the history has (\d+) rows?$")]
	public void RowCount(int count) => world.History.Entries.Count.ShouldBe(count);

	[Then(@"^the history remembers nothing at all$")]
	public void RemembersNothingAtAll() => world.History.Entries.ShouldBeEmpty();

	[Then(@"^the newest row in the history is alert (\d+)$")]
	public void NewestRow(long id) => world.History.Entries[0].Id.ShouldBe(id);

	[Then(@"^alert (\d+) can be replied to from the history$")]
	public void CanReplyFromHistory(long id) =>
		world.Remembered(id).ShouldNotBeNull().CanReply.ShouldBeTrue();

	[Then(@"^alert (\d+) cannot be replied to from the history$")]
	public void CannotReplyFromHistory(long id) =>
		world.Remembered(id).ShouldNotBeNull().CanReply.ShouldBeFalse();

	[Then(@"^alert (\d+)'s history row opens$")]
	public void RowOpens(long id) =>
		world.Remembered(id).ShouldNotBeNull().HasDetail.ShouldBeTrue();

	[Then(@"^alert (\d+)'s history row does not open$")]
	public void RowDoesNotOpen(long id) =>
		world.Remembered(id).ShouldNotBeNull().HasDetail.ShouldBeFalse();

	/// <summary>The stored status a feature file's plain words mean.</summary>
	private static string Status(string outcome) => outcome switch
	{
		"outstanding" => AlertHistoryStatus.Outstanding,
		"acknowledged" => AlertHistoryStatus.Acknowledged,
		"answered" => AlertHistoryStatus.Answered,
		"closed" => AlertHistoryStatus.Closed,
		"expired" => AlertHistoryStatus.Expired,
		"passed back" => AlertHistoryStatus.PassedOn,
		_ => outcome,
	};
}
