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
		/// A text <c>set</c> changes only the policies it names (measured on 6.49.21 and 7.x): <c>ftp</c> grants ftp and keeps
		/// the rest, <c>!write</c> denies write. So change a loaded group's members rather than assigning a list of the
		/// granted ones alone, which would keep every other policy the group had:
		/// <c>group.Policy = group.Policy.Value!.Without(UserPolicy.Write).With(TikNegatableValue&lt;UserPolicy&gt;.Not(UserPolicy.Write))</c>.
		/// An add denies every policy it does not name.
		/// </remarks>
		[TikProperty("policy", WinboxLabel = "Policies")]
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
