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
		[TikProperty("name", WinboxLabel = "Name")]
		public TikField<string?> Name { get; set; }

		/// <summary>
		/// policy — every policy, granted or denied: RouterOS prints the ones the group lacks negated
		/// (<c>read,write,test,!local,!telnet,…</c>), in its own order.
		/// </summary>
		/// <remarks>
		/// The value is the group's whole set: assigning the granted policies alone (<c>read,write</c>) leaves the group with
		/// those and no other. RouterOS's text <c>set</c> changes only the policies it names (measured on 6.49.21 and 7.x), so
		/// an update sends every policy the loaded value had and the new one leaves out as denied
		/// (<see cref="TikPropertyAttribute.SetKeepsUnnamedMembers"/>); an update of a group that was never loaded is refused,
		/// since only the loaded value names the policies this router has. An add denies every policy it does not name.
		/// </remarks>
		[TikProperty("policy", WinboxLabel = "Policies", SetKeepsUnnamedMembers = true)]
		public TikField<TikValueList<TikNegatableValue<UserPolicy>>?> Policy { get; set; }

		/// <summary>
		/// Gets or sets the the group's skin.
		/// </summary>
		[TikProperty("skin", WinboxLabel = "Skin")]
		public TikField<string?> Skin { get; set; }

		/// <summary>
		/// Gets or sets the comment associated with the group.
		/// </summary>
		[TikProperty("comment")]
		public TikField<string?> Comment { get; set; }
	}
}
