﻿using Backtrace.Interfaces;
using Backtrace.Model;
using Backtrace.Model.Database;
using Backtrace.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DynamicProxyGenAssembly2")]
namespace Backtrace.Services
{
    /// <summary>
    /// Backtrace Database Context
    /// </summary>
    internal class BacktraceDatabaseContext<T> : IBacktraceDatabaseContext<T>
    {
        /// <summary>
        /// Database cache
        /// </summary>
        internal Dictionary<int, List<BacktraceDatabaseEntry<T>>> BatchRetry = new Dictionary<int, List<BacktraceDatabaseEntry<T>>>();

        /// <summary>
        /// Total entries in BacktraceDatabase
        /// </summary>
        internal int totalEntries = 0;

        /// <summary>
        /// Path to database directory 
        /// </summary>
        private readonly string _path;

        /// <summary>
        /// Maximum number of retries
        /// </summary>
        private readonly int _retryNumber;

        /// <summary>
        /// Entry order
        /// </summary>
        public RetryOrder _retryOrder { get; }

        /// <summary>
        /// Initialize new instance of Backtrace Database Context
        /// </summary>
        /// <param name="path">Path to database directory</param>
        /// <param name="retryNumber">Total number of retries</param>
        /// <param name="retryOrder">Entry order</param>
        public BacktraceDatabaseContext(string path, uint retryNumber, RetryOrder retryOrder)
        {
            _path = path;
            _retryNumber = checked((int)retryNumber);
            _retryOrder = retryOrder;
            SetupBatch();
        }

        /// <summary>
        /// Setup cache 
        /// </summary>
        private void SetupBatch()
        {
            if (_retryNumber == 0)
            {
                throw new ArgumentException($"{nameof(_retryNumber)} have to be greater than 0!");
            }
            for (int i = 0; i < _retryNumber; i++)
            {
                BatchRetry[i] = new List<BacktraceDatabaseEntry<T>>();
            }
        }

        /// <summary>
        /// Add new entry to database
        /// </summary>
        /// <param name="backtraceData">Diagnostic data that should be stored in database</param>
        /// <returns>New instance of DatabaseEntry</returns>
        public virtual BacktraceDatabaseEntry<T> Add(BacktraceData<T> backtraceData)
        {
            var entry = new BacktraceDatabaseEntry<T>(backtraceData, _path);
            totalEntries++;
            entry.InUse = true;
            BatchRetry[0].Add(entry);
            return entry;
        }

        /// <summary>
        /// Check if any entry exists
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        public bool Any(BacktraceDatabaseEntry<T> entry)
        {
            return BatchRetry.SelectMany(n => n.Value).Any(n => n.Id == entry.Id);
        }

        /// <summary>
        /// Check if any entry exists
        /// </summary>
        public bool Any()
        {
            return totalEntries != 0;
        }

        /// <summary>
        /// Delete existing entry from database
        /// </summary>
        /// <param name="entry">Database entry to delete</param>
        public virtual void Delete(BacktraceDatabaseEntry<T> entry)
        {
            if (entry == null)
            {
                return;
            }
            foreach (var key in BatchRetry.Keys)
            {
                foreach (var value in BatchRetry[key])
                {
                    if (value.Id == entry.Id)
                    {
                        value.Delete();
                        BatchRetry[key].Remove(value);
                        totalEntries--;
                        return;
                    }
                }
            }
            return;
        }

        /// <summary>
        /// Increment retry time for current entry
        /// </summary>
        /// <param name="entry">Database entry to move move in memory cache</param>
        public void MoveNext()
        {
            RemoveLast();
            MoveRest();
        }

        private void MoveRest()
        {
            for (int i = BatchRetry.Keys.Count - 2; i >= 0; i--)
            {
                var temp = BatchRetry[i];
                BatchRetry[i] = new List<BacktraceDatabaseEntry<T>>();
                BatchRetry[i + 1] = temp;
            }
        }

        private void RemoveLast()
        {
            var currentBatch = BatchRetry.Last();
            var total = currentBatch.Value.Count - 1;
            for (int i = 0; i < total; i++)
            {
                var value = currentBatch.Value[i];
                value.Delete();
                totalEntries--;
            }
        }

        /// <summary>
        /// Increment retry time for current entry
        /// </summary>
        /// <param name="entry">Database entry to move move in memory cache</param>
        public virtual void MoveNext(BacktraceDatabaseEntry<T> entry)
        {
            foreach (var key in BatchRetry.Keys)
            {
                foreach (var value in BatchRetry[key])
                {
                    if (value.Id == entry.Id)
                    {
                        if (key + 1 <= _retryNumber)
                        {
                            BatchRetry[key + 1].Add(value);
                        }
                        else
                        {
                            value.Delete();
                            totalEntries--;
                        }
                        BatchRetry[key].Remove(value);
                        return;
                    }
                }
            }
            return;
        }

        /// <summary>
        /// Get all database entryes
        /// </summary>
        /// <returns>all existing database entries</returns>
        public IEnumerable<BacktraceDatabaseEntry<T>> Get()
        {
            return BatchRetry.SelectMany(n => n.Value);
        }

        /// <summary>
        /// Get total number of entries in database
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            return totalEntries;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            BatchRetry.Clear();
        }

        /// <summary>
        /// Delete all entries from database
        /// </summary>
        public void Clear()
        {
            var entries = BatchRetry.SelectMany(n => n.Value);
            foreach (var entry in entries)
            {
                entry.Delete();
            }
            totalEntries = 0;
        }

        /// <summary>
        /// Add existing entry to database
        /// </summary>
        /// <param name="backtraceEntry">Database entry</param>
        public void Add(BacktraceDatabaseEntry<T> backtraceEntry)
        {
            BatchRetry[0].Add(backtraceEntry);
            totalEntries++;
        }

        /// <summary>
        /// Get last exising database entry. Method returns entry based on order in Database
        /// </summary>
        /// <returns>First Backtrace database entry</returns>
        public BacktraceDatabaseEntry<T> LastOrDefault()
        {
            return _retryOrder == RetryOrder.Stack
                    ? GetLastEntry()
                    : GetFirstEntry();
        }

        /// <summary>
        /// Get first exising database entry. Method returns entry based on order in Database
        /// </summary>
        /// <returns>First Backtrace database entry</returns>
        public BacktraceDatabaseEntry<T> FirstOrDefault()
        {
            return _retryOrder == RetryOrder.Stack
                    ? GetFirstEntry()
                    : GetLastEntry();
        }

        /// <summary>
        /// Get first entry in in-cache BacktraceDatabase
        /// </summary>
        /// <returns>First database entry</returns>
        private BacktraceDatabaseEntry<T> GetFirstEntry()
        {
            for (int i = 0; i < BatchRetry.Count; i++)
            {
                if (BatchRetry[i].Any(n => !n.InUse))
                {
                    var entry = BatchRetry[i].First(n => !n.InUse);
                    entry.InUse = true;
                    return entry;
                }
            }
            return null;
        }

        /// <summary>
        /// Get last entry in in-cache BacktraceDatabase
        /// </summary>
        /// <returns>Last database entry</returns>
        private BacktraceDatabaseEntry<T> GetLastEntry()
        {
            for (int i = BatchRetry.Count -1 ; i >= 0; i--)
            {
                if (BatchRetry[i].Any(n => !n.InUse))
                {
                    var entry = BatchRetry[i].Last(n => !n.InUse);
                    entry.InUse = true;
                    return entry;
                }
            }
            return null;
        }

    }
}
