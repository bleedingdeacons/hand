using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>
/// Alerts arriving, and what the handset has to show for them: the card,
/// the notification, the alarm.
/// </summary>
[Binding]
public sealed class AlertSteps(World world)
{
	[Given(@"^Reach is holding a (red|yellow|blue) alert (\d+)$")]
	public void ReachIsHolding(string level, long id) =>
		world.Reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok(
			[world.Alert(id, World.Level(level))]);

	[Given(@"^Reach is holding nothing$")]
	public void ReachIsHoldingNothing() =>
		world.Reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok([]);

	[When(@"^a (red|yellow|blue) alert (\d+) arrives by push$")]
	public async Task ArrivesByPush(string level, long id) =>
		await world.Handset.HandlePushAsync(world.Alert(id, World.Level(level)));

	[When(@"^a (red|yellow|blue) informational alert (\d+) arrives by push$")]
	public async Task InformationalArrives(string level, long id) =>
		await world.Handset.HandlePushAsync(
			world.Alert(id, World.Level(level), HandAlert.ResponseNone));

	[When(@"^a (red|yellow|blue) alert (\d+) with (a|no) contact arrives by push$")]
	public async Task ArrivesWithContact(string level, long id, string has)
	{
		var alert = world.Alert(id, World.Level(level));
		alert.HasContact = string.Equals(has, "a", StringComparison.Ordinal);

		await world.Handset.HandlePushAsync(alert);
	}

	[When(@"^a (red|yellow|blue) alert (\d+) that expired (\d+) minutes ago arrives by push$")]
	public async Task ExpiredArrives(string level, long id, int minutes) =>
		await world.Handset.HandlePushAsync(world.Alert(
			id,
			World.Level(level),
			expiresAt: DateTimeOffset.UtcNow.AddMinutes(-minutes).ToUnixTimeSeconds()));

	[When(@"^a (red|yellow|blue) alert (\d+) that never expires arrives by push$")]
	public async Task NeverExpiresArrives(string level, long id) =>
		await world.Handset.HandlePushAsync(world.Alert(id, World.Level(level), expiresAt: 0));

	[When(@"^a (red|yellow|blue) alert (\d+) with nothing but a subject arrives by push$")]
	public async Task SubjectOnlyArrives(string level, long id)
	{
		var alert = world.Alert(id, World.Level(level));
		alert.Body = string.Empty;
		alert.Reference = string.Empty;

		await world.Handset.HandlePushAsync(alert);
	}

	[When(@"^the handset polls$")]
	public async Task Polls() => await world.Handset.RefreshAsync();

	[When(@"^alert (\d+) arrives again by poll$")]
	public async Task ArrivesAgain(long id)
	{
		world.Reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok([Copy(id)]);

		await world.Handset.RefreshAsync();
	}

	[When(@"^alert (\d+) arrives again by poll, this time with (a|no) contact$")]
	public async Task ArrivesAgainWithContact(long id, string has)
	{
		var copy = Copy(id);
		copy.HasContact = string.Equals(has, "a", StringComparison.Ordinal);

		world.Reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok([copy]);

		await world.Handset.RefreshAsync();
	}

	[Then(@"^alert (\d+) is on screen$")]
	public void OnScreen(long id) => world.OnScreen(id).ShouldNotBeNull();

	[Then(@"^alert (\d+) is no longer on screen$")]
	public void NotOnScreen(long id) => world.OnScreen(id).ShouldBeNull();

	[Then(@"^there (?:is|are) (\d+) alerts? on screen$")]
	public void HowManyOnScreen(int count) => world.Handset.Active.Count.ShouldBe(count);

	[Then(@"^nothing is on screen$")]
	public void NothingOnScreen() => world.Handset.Active.ShouldBeEmpty();

	[Then(@"^alert (\d+) was shown in the notification tray$")]
	public void WasPresented(long id) => world.Presenter.Presented.ShouldContain(a => a.Id == id);

	[Then(@"^alert (\d+) was never shown in the notification tray$")]
	public void WasNotPresented(long id) => world.Presenter.Presented.ShouldNotContain(a => a.Id == id);

	[Then(@"^alert (\d+)'s notification was taken down$")]
	public void WasDismissed(long id) => world.Presenter.Dismissed.ShouldContain(id);

	[Then(@"^the notification was posted (silently|audibly)$")]
	public void PostedHow(string how) =>
		world.Presenter.PresentedSilently.ShouldContain(string.Equals(how, "silently", StringComparison.Ordinal));

	[Then(@"^the alarm is sounding$")]
	public void AlarmSounding() => world.Alarm.IsSounding.ShouldBeTrue();

	[Then(@"^the alarm is silent$")]
	public void AlarmSilent() => world.Alarm.IsSounding.ShouldBeFalse();

	[Then(@"^the alarm never sounded$")]
	public void AlarmNeverSounded() => world.Alarm.Started.ShouldBeEmpty();

	[Then(@"^the alarm was started once$")]
	public void AlarmStartedOnce() => world.Alarm.Started.Count.ShouldBe(1);

	[Then(@"^the alarm was raised (silently|audibly)$")]
	public void AlarmRaisedHow(string how) =>
		world.Alarm.StartedSilently.ShouldContain(string.Equals(how, "silently", StringComparison.Ordinal));

	[Then(@"^alert (\d+) offers its contact details$")]
	public void OffersContact(long id) => world.OnScreen(id).ShouldNotBeNull().HasContact.ShouldBeTrue();

	[Then(@"^alert (\d+) offers no contact details$")]
	public void OffersNoContact(long id) => world.OnScreen(id).ShouldNotBeNull().HasContact.ShouldBeFalse();

	[Then(@"^alert (\d+) is still outstanding$")]
	public void StillOutstanding(long id) =>
		world.OnScreen(id).ShouldNotBeNull().IsSettled.ShouldBeFalse();

	/// <summary>
	/// The same alert as Reach would send it a second time — a separate
	/// object, because that is what a second delivery is. Handing the very
	/// object already on screen back to the loop would prove nothing about
	/// how duplicates are matched.
	/// </summary>
	private HandAlert Copy(long id)
	{
		var original = world.Built[id];

		return new HandAlert
		{
			Id = original.Id,
			Kind = original.Kind,
			Source = original.Source,
			Priority = original.Priority,
			Level = original.Level,
			Response = original.Response,
			Title = original.Title,
			Body = original.Body,
			Reference = original.Reference,
			CreatedAt = original.CreatedAt,
			ExpiresAt = original.ExpiresAt,
			MessageUuid = original.MessageUuid,
			HasContact = original.HasContact,
		};
	}
}
