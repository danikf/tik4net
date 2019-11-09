namespace tik4net.Objects.User
{
	/// <summary>
	/// Access to a user setting.
	/// </summary>
	[TikEntity("/user")]
	public class User
	{
		/// <summary>
		/// Gets the user's entry ID.
		/// </summary>
		[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
		public string Id { get; private set; }

		/// <summary>
		/// Gets or sets the user's name.
		/// </summary>
		[TikProperty("name", IsMandatory = true)]
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the group that the use is member of.
		/// </summary>
		[TikProperty("group")]
		public string Group { get; set; }

		/// <summary>
		/// Gets the time when the user has last logged in.
		/// </summary>
		[TikProperty("last-logged-in", IsReadOnly = true)]
		public string LastLoggedIn { get; private set; }

		/// <summary>
		/// Gets or sets a value indicating whether the user is disabled. 
		/// </summary>
		[TikProperty("disabled")]
		public bool Disabled { get; set; }

		/// <summary>
		/// Gets or sets a comment associated with the user. 
		/// </summary>
		[TikProperty("comment")]
		public string Comment { get; set; }

	}
}
