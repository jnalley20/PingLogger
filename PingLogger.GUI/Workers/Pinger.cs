﻿using System;
using System.Collections.Generic;
using System.Text;
using Serilog;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using PingLogger.GUI.Models;

namespace PingLogger.GUI.Workers
{
	public class Pinger : IDisposable
	{
		private readonly Host Host;
		public bool Running = false;
		private bool stopping = false;
		private readonly ILogger Logger;
		private Thread RunThread;
		private Ping pingSender = new Ping();
		public System.Collections.Concurrent.BlockingCollection<Reply> Replies = new System.Collections.Concurrent.BlockingCollection<Reply>();
		/// <summary>
		/// This class is where all of the actual pinging work is done.
		/// I creates a thread that loops until canceled.
		/// The thread fires an event to do the actual pinging. 
		/// </summary>
		/// <param name="host">The host that will be pinged.</param>
		/// <param name="defaultSilent">Set this to true to prevent Serilog from printing to the console.</param>
		public Pinger(Host host, bool defaultSilent = false, int daysToKeep = 7)
		{
			Host = host;
			if (!Directory.Exists("./Logs"))
				Directory.CreateDirectory("./Logs");
			//Check to see if just this is supposed to be silent, or if it's app-wide setting
			var outputTemp = "[{Timestamp:HH:mm:ss} {Level:u4}] {Message:lj}{NewLine}{Exception}";
			var errorOutputTemp = "[{Timestamp:HH:mm:ss} {Level:u5}] {Message:lj}{NewLine}{Exception}";

			var filePath = "./Logs/" + Host.HostName + ".log";
			var errorPathName = "./Logs/" + Host.HostName + "-Errors.log";
			var warnPathName = "./Logs/" + Host.HostName + "-Warnings.log";

#if DEBUG
			var debugOutputTemp = "[{Timestamp:HH:mm:ss.fff} {Level}] ({ThreadId}) {Message:lj}{NewLine}{Exception}";
			var debugPathName = "./Logs/" + Host.HostName + "-Debug.log";
#endif
			Logger = new LoggerConfiguration()
#if DEBUG
				.Enrich.With(new ThreadIdEnricher())
				.MinimumLevel.Verbose()
#endif
				.WriteTo.Logger(
					l => l.Filter.ByIncludingOnly(e => e.Level == Serilog.Events.LogEventLevel.Error)
					.WriteTo.File(
						errorPathName,
						restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error,
						retainedFileCountLimit: daysToKeep,
						shared: true,
						outputTemplate: errorOutputTemp
						)
					)
				.WriteTo.Logger(
					l => l.Filter.ByIncludingOnly(e => e.Level == Serilog.Events.LogEventLevel.Warning)
					.WriteTo.File(
						warnPathName,
						shared: true,
						retainedFileCountLimit: daysToKeep,
						outputTemplate: outputTemp
						)
					)
#if DEBUG
					.WriteTo.Logger(
					l => l.Filter.ByIncludingOnly(e => e.Level == Serilog.Events.LogEventLevel.Debug)
					.WriteTo.File(
						debugPathName,
						shared: true,
						retainedFileCountLimit: daysToKeep,
						restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug,
						outputTemplate: debugOutputTemp
						)
					)
#endif
				.WriteTo.File(
						filePath,
						shared: true,
						outputTemplate: outputTemp,
						retainedFileCountLimit: daysToKeep,
						restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information
					)
				.CreateLogger();

			//Check to make sure the packet size isn't too large. Don't want to abuse this.
			if (Host.PacketSize > 65500)
			{
				Logger.Error("Packet size too large. Resetting to 20000 bytes");
				Host.PacketSize = 65500;
			}
			//Make sure that the interval isn't too short. If you set it to be too frequent, it might get flagged as DDoS attack.
			if (Host.Interval < 500)
			{
				Logger.Error("Interval too short. Setting to 500ms");
				Host.Interval = 500;
			}
			//Verify that the IP stored in the settings file matches what it currently resolves to.
			//Mostly in cases of local network and DHCP
			Logger.Information("Verifying IP address of hostname is current.");
			foreach (var ip in Dns.GetHostAddresses(Host.HostName))
			{
				Logger.Debug($"IP: {ip.ToString()}");
				if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
				{
					if (ip.ToString() == Host.IP)
					{
						Logger.Information("IP matches. Continuing");
					}
					else
					{
						Logger.Warning("IP address does not match last stored. Saving new IP address");
						Host.IP = ip.ToString();
					}
					break;
				}
			}
		}
		/// <summary>
		/// Get the host info from this class. In case we had to make modifications to it on startup.
		/// </summary>
		/// <returns>Most recent Host class</returns>
		public Host UpdateHost()
		{
			Logger.Debug("UpdateHost() Requested");
			return Host;
		}

		private readonly Random random = new Random();
		/// <summary>
		/// Generates a random string with the specified length.
		/// </summary>
		/// <param name="length">Number of characters in the string</param>
		/// <returns>Random string of letters and numbers</returns>
		private string RandomString(int length)
		{
			Logger.Debug($"Random string of {length} characters requested.");
			const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			return new string(Enumerable.Repeat(chars, length)
			  .Select(s => s[random.Next(s.Length)]).ToArray());
		}
		/// <summary>
		/// Starts the primary thread where all the work is done. 
		/// Outputs all of the hosts info so that if we're starting it up, the user can see if anything is wrong, allowing them time to make changes.
		/// </summary>
		public void Start()
		{
			Logger.Debug("Start()");
			RunThread = new Thread(new ThreadStart(StartLogging));
			Logger.Information("Starting ping logging for host {0} ({1})", Host.HostName, Host.IP);
			Logger.Information("Using the following options:");
			Logger.Information("Threshold: {0}ms", Host.Threshold);
			Logger.Information("Timeout: {0}ms", Host.Timeout);
			Logger.Information("Interval: {0}ms", Host.Interval);
			Logger.Information("Packet Size: {0} bytes", Host.PacketSize);

			Logger.Debug("RunThread.Start()");
			RunThread.Start();
		}
		/// <summary>
		/// Primary thread that is spun up. Doesn't do the actual pinging, that's handled by an event.
		/// </summary>
		private void StartLogging()
		{
			Logger.Debug("StartLogging() Called.");
			pingSender.PingCompleted += new PingCompletedEventHandler(SendPing);
			PingOptions options = new PingOptions();
			AutoResetEvent waiter = new AutoResetEvent(false);

			//Generate a string that's as long as the packet size. 
			//This is outside of the loop, so it's going to be the same while the thread is running.
			//If it's restarted, we generate a new string. 
			string data = RandomString(Host.PacketSize);
			Logger.Debug($"Data string: {data}");

			byte[] buffer = Encoding.ASCII.GetBytes(data);

			Running = true;
			while (Running)
			{
				Logger.Debug($"Running: {Running.ToString()}");
				Logger.Debug($"stopping: {stopping.ToString()}");
				options.DontFragment = false;
				if (stopping)
				{
					Running = false;
				}
				else
				{
					Logger.Debug("Sending Async Ping");
					try
					{
						pingSender.SendAsync(Host.IP, Host.Timeout, buffer, options, waiter);
						waiter.WaitOne();
						Thread.Sleep(Host.Interval);
						Logger.Debug($"Waited {Host.Interval}ms");
					}
					catch
					{
						Logger.Debug("Thread Interrupted");
					}
				}
			}
			Logger.Debug("PingSender.Dispose()");
			pingSender.Dispose();
			Logger.Debug("SendPing() Ended");
		}
		/// <summary>
		/// Change the 'stopping' variable to true so that the thread can dispose of the pingSender properly, then allows the thread to exit safely.
		/// </summary>
		public void Stop()
		{
			stopping = true;
			Logger.Information("Stopping ping logger for host {0} ({1})", Host.HostName, Host.IP);
			Logger.Debug("SendAsyncCancel()");
			pingSender.SendAsyncCancel();
			try
			{
				RunThread.Interrupt();
			}
			catch
			{
				Logger.Debug("Thread Interrupted");
			}
		}
		/// <summary>
		/// The main workhorse of the class. 
		/// This event is called with every ping 
		/// Most of the options will never be hit, but I included them as a just-in-case measure.
		/// </summary>
		private void SendPing(object sender, PingCompletedEventArgs e)
		{
			Logger.Debug("SendPing() called");
			if (e.Cancelled)
			{
				Logger.Information("Ping canceled.");
				((AutoResetEvent)e.UserState).Set();
				return;
			}
			if (e.Error != null)
			{
				Logger.Information("Ping canceled.");
				//Logger.Debug(e.Error.ToString());
				((AutoResetEvent)e.UserState).Set();
				return;
			}
			var reply = e.Reply;
			bool timedOut = false;
			bool success = false;
			switch (reply.Status)
			{
				case IPStatus.Success:
					Logger.Debug("Ping Success");
					//Ping was successful. Check to see if the round trip time was greater than the threshold.
					//If it is, then we change the output to be a warning, making it easy to track down in the log files.
					if (reply.RoundtripTime >= Host.Threshold)
					{
						Logger.Warning("Pinged {0} ({1}) RoundTrip: {2}ms (Over Threshold) TTL: {3}", Host.HostName, Host.IP.ToString(), reply.RoundtripTime, reply.Options.Ttl);
						success = true;
					}
					else
					{
						Logger.Information("Pinged {0} ({1}) RoundTrip: {2}ms TTL: {3}", Host.HostName, Host.IP.ToString(), reply.RoundtripTime, reply.Options.Ttl);
						success = true;
					}
					break;
				//These indicate that there was a problem somewhere along the way. 
				case IPStatus.DestinationHostUnreachable:
					Logger.Error("Destination host unreachable.");
					break;
				case IPStatus.DestinationNetworkUnreachable:
					Logger.Error("Destination network unreachable.");
					break;
				case IPStatus.DestinationUnreachable:
					Logger.Error("Destination unreachable, cause unknown.");
					break;
				case IPStatus.HardwareError:
					Logger.Error("Ping failed due to hardware.");
					break;
				case IPStatus.TimedOut:
					Logger.Debug("Ping Timed Out");
					Logger.Error("Ping timed out to host {0} ({1}). Timeout is {2}ms", Host.HostName, Host.IP.ToString(), Host.Timeout);
					timedOut = true;
					break;
			}
			var LogReply = new Reply
			{
				Host = Host,
				DateTime = DateTime.Now,
				Ttl = reply.Options?.Ttl,
				RoundTrip = reply.RoundtripTime,
				TimedOut = timedOut,
				Succeeded = success
			};
			Replies.Add(LogReply);
			Logger.Debug("Ping Ended");
			((AutoResetEvent)e.UserState).Set();
		}

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			Logger.Debug("Dispose() called");
			if (!disposedValue)
			{
				if (disposing)
				{
					Stop();
					Log.CloseAndFlush();
					RunThread.Join();
				}
				disposedValue = true;
			}
		}

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
		}
		#endregion
	}
}
