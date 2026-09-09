namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Whether this phone will still show a notification Hand posts.
///
/// <para>Separate from <see cref="IPlatformAlertPresenter"/>, which
/// <i>asks</i> for the permission at the moment it needs one. This only
/// reads, and never prompts: it is called to draw an indicator on a
/// settings page, and a settings page that raised a permission sheet on
/// arrival would be one nobody opens twice.</para>
///
/// <para>Answers false when it cannot tell. An indicator that
/// under-promises gets checked; one that over-promises is how a handset
/// goes quiet.</para>
/// </summary>
public interface INotificationPermission
{
	Task<bool> IsGrantedAsync();
}
