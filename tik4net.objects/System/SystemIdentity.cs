namespace tik4net.Objects.System
{
	/// <summary>
    /// Gets the info provided by
	/// /system/identity
	/// </summary>
	/// <remarks>
	/// A singleton — the router holds exactly one identity and it has no <c>.id</c>, so
	/// <c>connection.Save(identity)</c> issues <c>/system/identity/set name=…</c>. Read it with
	/// <c>LoadSingle</c>.
	/// </remarks>
	[TikEntity("/system/identity", IsSingleton = true)]
	public class SystemIdentity
	{
		/// <summary>
		/// Gets or sets the name of the system.
		/// </summary>
		[TikProperty("name", IsMandatory = true)]
		public string? Name { get; set; }
	}
}
