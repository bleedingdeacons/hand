using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>
/// The removal notice — the one thing on the alert loop that is an
/// instruction to the app rather than something a responder is being
/// asked to act on.
/// </summary>
[Binding]
public sealed class EnrolmentSteps(World world)
{
	[When(@"^a removal notice arrives$")]
	public async Task RemovalNoticeArrives() =>
		await world.Handset.HandlePushAsync(Removal(expiresAt: 0));

	[When(@"^a removal notice that expired (\d+) minutes ago arrives$")]
	public async Task LateRemovalNoticeArrives(int minutes) =>
		await world.Handset.HandlePushAsync(
			Removal(DateTimeOffset.UtcNow.AddMinutes(-minutes).ToUnixTimeSeconds()));

	[Then(@"^Reach was never asked whether this handset is still enrolled$")]
	public void NeverChecked() => world.Reach.SessionChecks.ShouldBe(0);

	private HandAlert Removal(long expiresAt)
	{
		var notice = world.Alert(
			99,
			HandAlert.LevelRed,
			HandAlert.ResponseNone,
			HandAlert.KindDeviceRemoved,
			expiresAt);

		notice.Title = "This handset has been removed";

		return notice;
	}
}
