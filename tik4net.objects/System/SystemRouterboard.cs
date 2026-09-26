namespace tik4net.Objects.System
{
	/// <summary>
    /// Gets the infor provided by
	/// /system/routerboard 
	/// <para>
	/// Read-only: the menu offers <c>get</c>, <c>print</c>, <c>export</c> and <c>upgrade</c>, and no
	/// <c>add</c>/<c>set</c>/<c>remove</c>/<c>move</c> — measured on RouterBOARD hardware, since the menu
	/// does not exist on the CHR the test suite runs against (<c>/system/routerboard/print</c> there
	/// answers <i>no such command or directory (routerboard)</i>, which is why
	/// <c>EntityOperationMatrixTest</c> skips this entity rather than failing).
	/// </para>
	/// </summary>
	[TikEntity("/system/routerboard", SupportedOperations = TikEntityOperations.None)]
	public class SystemRouterboard
	{
		/// <summary>
		/// Gets a value indicating whether this hardware is a RouterBoard.
		/// </summary>
		[TikProperty("routerboard")]
		public TikValue<bool?> Routerboard { get; set; }

		/// <summary>
		/// Gets the name of the board. 
		/// </summary>
		[TikProperty("board-name")]
		public TikValue<string?> BoardName { get; set; }

		/// <summary>
		/// Gets the model of the board.
		/// </summary>
		[TikProperty("model")]
		public TikValue<string?> Model { get; set; }

		/// <summary>
		/// Gets the serial number of the board.
		/// </summary>
		[TikProperty("serial-number")]
		public TikValue<string?> SerialNumber { get; set; }

		/// <summary>
		/// Gets the firmware type of the board.
		/// </summary>
		[TikProperty("firmware-type")]
		public TikValue<string?> FirmwareType { get; set; }

		/// <summary>
		/// Gets the firmware version that was flashed by factory on delivery.
		/// </summary>
		[TikProperty("factory-firmware")]
		public TikValue<string?> FactoryFirmware { get; set; }

		/// <summary>
		/// Gets the firmware version that is currently running.
		/// </summary>
		[TikProperty("current-firmware")]
		public TikValue<string?> CurrentFirmware { get; set; }

		/// <summary>
		/// Gets the firmware version that is available for upgrade.
		/// </summary>
		[TikProperty("upgrade-firmware")]
		public TikValue<string?> UpgradeFirmware { get; set; }
	}
}
