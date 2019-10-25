namespace tik4net.Objects.System
{
	/// <summary>
    /// Gets the info provided by
	/// /system/identity
	/// </summary>
	[TikEntity("/system/identity")]
	public class SystemIdentity
	{
		/// <summary>
		/// Gets or sets the name of the system.
		/// </summary>
		[TikProperty("name", IsMandatory = true)]
		public string Name { get; set; }
	}
}