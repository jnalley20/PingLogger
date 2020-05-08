﻿using PingLogger.GUI.Models;
using PingLogger.GUI.Workers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PingLogger.GUI.Controls
{
	/// <summary>
	/// Interaction logic for PingControl.xaml
	/// </summary>
	public partial class PingControl : UserControl
	{
		readonly DispatcherTimer Timer;
		public Host PingHost;
		private Pinger Pinger;
		private readonly List<long> PingTimes = new List<long>();
		private int Timeouts = 0;
		private int Warnings = 0;
		private readonly SynchronizationContext syncCtx;
		private readonly bool LoadFromVar = false;
		private double PacketLoss = 0.0;
		private long TotalPings = 0;
		public PingControl()
		{
			InitializeComponent();
			Timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(100)
			};
			Timer.Tick += Timer_Tick;
			PingHost = new Host();
			syncCtx = SynchronizationContext.Current;
			Timer.Start();
		}

		public PingControl(Host _host, bool RunTimeAdded = false)
		{
			InitializeComponent();
			Timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(100)
			};
			Timer.Tick += Timer_Tick;
			PingHost = _host;
			LoadFromVar = true;
			syncCtx = SynchronizationContext.Current;
			Timer.Start();
			if (!RunTimeAdded)
				AutoStart();
		}
		void AutoStart()
		{
			if (Config.StartLoggersAutomatically)
			{
				StopBtn.IsEnabled = true;
				StartBtn.IsEnabled = false;
				HostNameBox.IsEnabled = false;
				IntervalBox.IsEnabled = false;
				WarningBox.IsEnabled = false;
				TimeoutBox.IsEnabled = false;
				PacketSizeBox.IsEnabled = false;
				Pinger = new Pinger(PingHost);
				Pinger.Start();
			}
		}
		public void DoStart()
		{
			StopBtn.IsEnabled = true;
			StartBtn.IsEnabled = false;
			HostNameBox.IsEnabled = false;
			IntervalBox.IsEnabled = false;
			WarningBox.IsEnabled = false;
			TimeoutBox.IsEnabled = false;
			PacketSizeBox.IsEnabled = false;
			Pinger = new Pinger(PingHost);
			Pinger.Start();
		}
		void Timer_Tick(object sender, EventArgs e)
		{
			if (Pinger != null && Pinger.Running)
			{
				Logger.Debug("Pinger not null & Pinger Running");
				StringBuilder sb = new StringBuilder();
				Logger.Debug($"Replies to parse: {Pinger.Replies.Count}");
				for (int i = 0; i < Pinger.Replies.Count - 1; i++)
				{
					Logger.Debug($"Reply #{i}");
					TotalPings++;
					var success = Pinger.Replies.TryTake(out Reply reply);
					if (success)
					{
						Logger.Debug("Ping Success");
						var line = $"[{ reply.DateTime.ToLongTimeString()}] ";
						if (reply.RoundTrip > 0)
						{
							PingTimes.Add(reply.RoundTrip);
							Logger.Debug($"RoundTrip Time > 0: {reply.RoundTrip}");
						}

						if (reply.TimedOut)
						{
							Timeouts++;
							Logger.Debug($"Reply timed out. Number of Timeouts: {Timeouts}");
							line += $"Timed out to host";
							PacketLoss = ((double)Timeouts / (double)TotalPings) * 100;
						}
						else
						{
							line += $"Ping round trip: {reply.RoundTrip}ms";
							if (reply.RoundTrip >= PingHost.Threshold)
							{
								Warnings++;
								line += " [Warning]";
							}
						}
						sb.Append(line);
						sb.Append(Environment.NewLine);
					}
				}
				PingStatusBox.Text += sb.ToString();
				var lines = PingStatusBox.Text.Split(Environment.NewLine).ToList();
				if (lines.Count() > 26)
				{
					Logger.Debug($"Lines in text box greater than 26, removing a line.");
					lines.RemoveAt(0);
					PingStatusBox.Text = string.Join(Environment.NewLine, lines);
				}

				if (PingTimes.Count > 0)
				{
					if (PingTimes.Count > 26)
						PingTimes.RemoveAt(0);

					avgPingLbl.Content = Math.Ceiling(PingTimes.Average()).ToString() + "ms";
				}
				timeoutLbl.Content = Timeouts.ToString();
				warningLbl.Content = Warnings.ToString();
				packetLossLabel.Content = $"{Math.Round(PacketLoss, 2)}%";
			}
			else
			{
				if (IPAddressBox.Text == "Invalid Host Name")
				{
					StartBtn.IsEnabled = false;
				}
				else
				{
					StartBtn.IsEnabled = true;
				}
			}
		}

		private void StartBtn_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				Logger.Debug($"StartBtn_Click");
				StopBtn.IsEnabled = true;
				StartBtn.IsEnabled = false;
				HostNameBox.IsEnabled = false;
				IntervalBox.IsEnabled = false;
				WarningBox.IsEnabled = false;
				TimeoutBox.IsEnabled = false;
				PacketSizeBox.IsEnabled = false;
				Pinger = new Pinger(PingHost);
				Logger.Debug($"Pinger instance created.");
				Pinger.Start();
				Logger.Debug($"Pinger started.");
			}
			catch (Exception ex)
			{
				Logger.Debug(ex.ToString());
			}
		}

		public void DoStop()
		{
			StopBtn.IsEnabled = false;
			StartBtn.IsEnabled = true;
			HostNameBox.IsEnabled = true;
			IntervalBox.IsEnabled = true;
			WarningBox.IsEnabled = true;
			TimeoutBox.IsEnabled = true;
			PacketSizeBox.IsEnabled = true;
			if (Pinger != null)
			{
				Pinger.Stop();
			}
		}

		private void StopBtn_Click(object sender, RoutedEventArgs e)
		{
			StopBtn.IsEnabled = false;
			StartBtn.IsEnabled = true;
			HostNameBox.IsEnabled = true;
			IntervalBox.IsEnabled = true;
			WarningBox.IsEnabled = true;
			TimeoutBox.IsEnabled = true;
			PacketSizeBox.IsEnabled = true;
			Pinger.Stop();
		}
		private void UpdateHost()
		{
			var index = Config.Hosts.IndexOf(PingHost);
			PingHost.HostName = HostNameBox.Text;
			PingHost.IP = IPAddressBox.Text;
			PingHost.Interval = Convert.ToInt32(IntervalBox.Text);
			PingHost.PacketSize = Convert.ToInt32(PacketSizeBox.Text);
			PingHost.Threshold = Convert.ToInt32(WarningBox.Text);
			PingHost.Timeout = Convert.ToInt32(TimeoutBox.Text);
			Config.Hosts[index] = PingHost;
		}
		private async void HostNameBox_LostFocus(object sender, RoutedEventArgs e)
		{
			if (PingHost.HostName != HostNameBox.Text)
			{
				StartBtn.IsEnabled = false;

				await UpdateIPBox();
				(this.Parent as TabItem).Header = $"Host: {HostNameBox.Text}";
				UpdateHost();
			}
		}

		private async void MainControl_Loaded(object sender, RoutedEventArgs e)
		{
			if (LoadFromVar)
			{
				HostNameBox.Text = PingHost.HostName;
				IntervalBox.Text = PingHost.Interval.ToString();
				WarningBox.Text = PingHost.Threshold.ToString();
				TimeoutBox.Text = PingHost.Timeout.ToString();
				PacketSizeBox.Text = PingHost.PacketSize.ToString();
			}

			await UpdateIPBox();
		}

		private async Task UpdateIPBox()
		{
			try
			{
				foreach (var ip in await Dns.GetHostAddressesAsync(HostNameBox.Text))
				{
					if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
					{
						syncCtx.Post(new SendOrPostCallback(o =>
						{
							IPAddressBox.Text = (string)o;
						}),
						ip.ToString()
						);
						PingHost.IP = ip.ToString();
						PingHost.HostName = HostNameBox.Text;
						break;
					}
				}
			}
			catch (Exception)
			{
				IPAddressBox.Text = "Invalid Host Name";
			}
			return;
		}

		private static readonly Regex regex = new Regex("[^0-9.-]+");
		private static bool IsNumericInput(string text)
		{
			return !regex.IsMatch(text);
		}

		private void IntervalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
		{
			e.Handled = !IsNumericInput(e.Text);
		}

		private void WarningBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
		{
			e.Handled = !IsNumericInput(e.Text);
		}

		private void TimeoutBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
		{
			e.Handled = !IsNumericInput(e.Text);
		}

		private void PacketSizeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
		{
			e.Handled = !IsNumericInput(e.Text);
		}

		private void IntervalBox_LostFocus(object sender, RoutedEventArgs e)
		{
			var interval = Convert.ToInt32(IntervalBox.Text);
			if (interval < 250)
			{
				IntervalBox.Text = "250";
				MessageBox.Show("Interval can not be less than 250ms!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			UpdateHost();
		}

		private void PacketSizeBox_LostFocus(object sender, RoutedEventArgs e)
		{
			var packetSize = Convert.ToInt32(PacketSizeBox.Text);
			if (packetSize > 65500)
			{
				PacketSizeBox.Text = "65500";
				MessageBox.Show("Packet size can not be larger than 65,500 bytes!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			if (packetSize == 0)
			{
				PacketSizeBox.Text = "1";
				MessageBox.Show("Packet size can not be smaller than 1 byte!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			UpdateHost();
		}

		private void HostNameBox_GotFocus(object sender, RoutedEventArgs e)
		{
			HostNameBox.SelectAll();
		}

		private void IntervalBox_GotFocus(object sender, RoutedEventArgs e)
		{
			IntervalBox.SelectAll();
		}

		private void WarningBox_GotFocus(object sender, RoutedEventArgs e)
		{
			WarningBox.SelectAll();
		}

		private void TimeoutBox_GotFocus(object sender, RoutedEventArgs e)
		{
			TimeoutBox.SelectAll();
		}

		private void PacketSizeBox_GotFocus(object sender, RoutedEventArgs e)
		{
			PacketSizeBox.SelectAll();
		}

		private void HostNameBox_MouseUp(object sender, MouseButtonEventArgs e)
		{
			HostNameBox.SelectAll();
		}

		private void WarningBox_LostFocus(object sender, RoutedEventArgs e)
		{
			UpdateHost();
		}

		private void TimeoutBox_LostFocus(object sender, RoutedEventArgs e)
		{
			UpdateHost();
		}

		private void HelpBtn_Click(object sender, RoutedEventArgs e)
		{
			var helpDlg = new HelpDialog();
			helpDlg.ShowDialog();
		}
	}
}