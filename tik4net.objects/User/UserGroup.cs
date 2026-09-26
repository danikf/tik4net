namespace tik4net.Objects.User
{
	/// <summary>
	/// Access to a group setting.
	/// </summary>
	[TikEntity("/user/group")]
	public class UserGroup
	{
		/// <summary>
		/// Gets the entry's ID
		/// </summary>
		[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
		public string? Id { get; private set; }

		/// <summary>
		/// Gets or sets the group name.
		/// </summary>
		[TikProperty("name")]
		public TikValue<string?> Name { get; set; }

		/// <summary>
		/// Gets or sets the group's policies as comma-separated list. 
		/// </summary>
		[TikProperty("policy")]
		public TikValue<string?> Policy { get; set; }

		/// <summary>
		/// Gets or sets the the group's skin.
		/// </summary>
		[TikProperty("skin")]
		public TikValue<string?> Skin { get; set; }

		/// <summary>
		/// Gets or sets the comment associated with the group.
		/// </summary>
		[TikProperty("comment")]
		public TikValue<string?> Comment { get; set; }
	}
}
