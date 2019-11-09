﻿namespace PingLogger.Misc
{
	public class Host
	{
		public string HostName { get; set; }
		public string IP { get; set; }
		public int Threshold { get; set; } = 500;
		public int PacketSize { get; set; } = 32;
		public int Interval { get; set; } = 1000;
		// Changed default to 2 seconds
		public int Timeout { get; set; } = 2000;
	}
}
