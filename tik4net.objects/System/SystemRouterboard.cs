namespace tik4net.Objects.System
{
	/// <summary>
    /// Gets the infor provided by
	/// /system/routerboard 
	/// </summary>
	[TikEntity("/system/routerboard", IsReadOnly = true)]
	public class SystemRouterboard
	{
		/// <summary>
		/// Gets a value indicating whether this hardware is a RouterBoard.
		/// </summary>
		[TikProperty("routerboard")]
		public bool Routerboard { get; set; }

		/// <summary>
		/// Gets the name of the board. 
		/// </summary>
		[TikProperty("board-name")]
		public string BoardName { get; set; }

		/// <summary>
		/// Gets the model of the board.
		/// </summary>
		[TikProperty("model")]
		public string Model { get; set; }

		/// <summary>
		/// Gets the serial number of the board.
		/// </summary>
		[TikProperty("serial-number")]
		public string SerialNumber { get; set; }

		/// <summary>
		/// Gets the firmware type of the board.
		/// </summary>
		[TikProperty("firmware-type")]
		public string FirmwareType { get; set; }

		/// <summary>
		/// Gets the firmware version that was flashed by factory on delivery.
		/// </summary>
		[TikProperty("factory-firmware")]
		public string FactoryFirmware { get; set; }

		/// <summary>
		/// Gets the firmware version that is currently running.
		/// </summary>
		[TikProperty("current-firmware")]
		public string CurrentFirmware { get; set; }

		/// <summary>
		/// Gets the firmware version that is available for upgrade.
		/// </summary>
		[TikProperty("upgrade-firmware")]
		public string UpgradeFirmware { get; set; }
	}
}