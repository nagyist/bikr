﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using SQLite;

namespace Bikr
{
	public struct TripDistanceStats {
		public double Daily { get; set; }
		public double Weekly { get; set; }
		public double Monthly { get; set; }

		public double PrevDay { get; set; }
		public double PrevWeek { get; set; }
		public double PrevMonth { get; set; }
	}

	public class DataApi
	{
		const string SumQuery = "select ifnull(sum(distance),0) from BikeTrip where `commit` >= ?";
		const string SumBetweenQuery = "select ifnull(sum(distance),0) from BikeTrip where `commit` >= ?1 and `commit` <= ?2";
		string dbPath;

		public DataApi (string dbPath)
		{
			this.dbPath = dbPath;
		}

		#if __ANDROID__
		public static DataApi Obtain (Android.Content.Context ctx)
		{
			var db = ctx.OpenOrCreateDatabase ("trips.db", Android.Content.FileCreationMode.WorldReadable, null);
			var path = db.Path;
			db.Close ();
			return new DataApi (path);
		}
		#endif

		public string DbPath {
			get { return dbPath; }
		}

		public async Task ClearDatabase ()
		{
			using (var connection = new SQLiteConnection (dbPath, true))
				await Task.Run (() => connection.DropTable<BikeTrip> ()).ConfigureAwait (false);
		}

		async Task<SQLiteConnection> GrabConnection ()
		{
			var connection = new SQLiteConnection (dbPath, true);
			await Task.Run (() => connection.CreateTable<BikeTrip> ()).ConfigureAwait (false);
			return connection;
		}

		public async Task AddTrip (BikeTrip trip)
		{
			// Sanitize trip date information
			if (trip.CommitTime == DateTime.MinValue)
				trip.CommitTime = DateTime.UtcNow;
			else if (trip.CommitTime.Kind != DateTimeKind.Utc)
				trip.CommitTime = trip.CommitTime.ToUniversalTime ();
			if (trip.StartTime.Kind != DateTimeKind.Utc)
				trip.StartTime = trip.StartTime.ToUniversalTime ();
			if (trip.EndTime.Kind != DateTimeKind.Utc)
				trip.EndTime = trip.EndTime.ToUniversalTime ();

			using (var connection = await GrabConnection ().ConfigureAwait (false))
				await Task.Run (() => connection.Insert (trip)).ConfigureAwait (false);
		}

		public async Task<List<BikeTrip>> GetTripsAfter (DateTime dt)
		{
			if (dt.Kind != DateTimeKind.Utc)
				dt = dt.ToUniversalTime ();
			using (var connection = await GrabConnection ().ConfigureAwait (false)) {
				return await Task.Run (() => connection
					.Table<BikeTrip> ()
				    .Where (bt => bt.CommitTime >= dt)
					.ToList ()
				).ConfigureAwait (false);
			}
		}

		public Task<TripDistanceStats> GetStats ()
		{
			return GetStats (DateTime.Now);
		}

		public async Task<TripDistanceStats> GetStats (DateTime now)
		{
			using (var connection = await GrabConnection ().ConfigureAwait (false)) {
				if (!connection.Table<BikeTrip> ().Any ())
					return new TripDistanceStats ();

				return await Task.Run (() => new TripDistanceStats {
					Daily = FetchStatsFromDate (connection, now.DayStart ()),
					Weekly = FetchStatsFromDate (connection, now.WeekStart ()),
					Monthly = FetchStatsFromDate (connection, now.MonthStart ()),
					PrevDay = FetchStatsBetweenDates (connection, now.DayStart ().PreviousDay (), now.DayStart ()),
					PrevWeek = FetchStatsBetweenDates (connection, now.WeekStart ().PreviousWeek (), now.WeekStart ()),
					PrevMonth = FetchStatsBetweenDates (connection, now.MonthStart ().PreviousMonth (), now.MonthStart ()),
				}).ConfigureAwait (false);
			}
		}

		double FetchStatsFromDate (SQLiteConnection connection, DateTime dt)
		{
			return connection.ExecuteScalar<double> (SumQuery, dt.ToUniversalTime ());
		}

		double FetchStatsBetweenDates (SQLiteConnection connection, DateTime startTime, DateTime endTime)
		{
			return connection.ExecuteScalar<double> (SumBetweenQuery,
			                                         startTime.ToUniversalTime (),
			                                         endTime.ToUniversalTime ());
		}
	}
}

