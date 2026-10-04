using System;
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
		public string? Id { get; private set; }

		/// <summary>
		/// Gets or sets the user's name.
		/// </summary>
		[TikProperty("name", WinboxLabel = "Name")]
		public TikField<string?> Name { get; set; }

		/// <summary>
		/// Gets or sets the group that the use is member of.
		/// </summary>
		[TikProperty("group", WinboxLabel = "Group")]
		public TikField<string?> Group { get; set; }

		/// <summary>
		/// Gets the time when the user has last logged in.
		/// Read from either spelling (7.x <c>2026-07-25 10:24:52</c>, 6.x <c>jul/25/2026 10:24:52</c>); no time zone is applied
		/// (<see cref="DateTimeKind.Unspecified"/>).
		/// </summary>
		[TikProperty("last-logged-in", IsReadOnly = true, WinboxLabel = "Last Logged In")]
		public TikField<DateTime?> LastLoggedIn { get; private set; }

		/// <summary>
		/// Gets or sets a value indicating whether the user is disabled. 
		/// </summary>
		[TikProperty("disabled")]
		public TikField<bool?> Disabled { get; set; }

		/// <summary>
		/// Gets or sets a comment associated with the user. 
		/// </summary>
		[TikProperty("comment")]
		public TikField<string?> Comment { get; set; }

	}
}
