using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Dogpatch.Gameplay
{
    // Rate-limits repeated log lines: an identical message (or exception type and message) prints at
    // most once per 5 s, and the next one that gets through, or the flush after the burst ends,
    // carries how many were dropped. On iOS every Debug.Log captures a stack trace, so a line
    // repeated 28 times a second is a steady main-thread cost. Installed before the first scene loads.
    public sealed class LogLimiter : ILogHandler
    {
        const double WindowSeconds = 5;

        sealed class Entry
        {
            public double Until;
            public int Dropped;
            public LogType Type;
        }

        readonly ILogHandler inner;
        readonly Dictionary<string, Entry> seen = new Dictionary<string, Entry>();
        readonly List<(string message, Entry entry)> flush = new List<(string, Entry)>();
        readonly List<string> expired = new List<string>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double nextPrune;

        LogLimiter(ILogHandler inner) => this.inner = inner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            if (Debug.unityLogger.logHandler is LogLimiter) return;
            Debug.unityLogger.logHandler = new LogLimiter(Debug.unityLogger.logHandler);
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            string message = args == null || args.Length == 0 ? format : string.Format(format, args);
            int dropped;
            lock (seen)
            {
                if (!Allow(message, logType, out dropped)) return;
            }
            Flush();
            if (dropped > 0) inner.LogFormat(logType, context, "{0} [repeated {1} more times in the last 5 s]", message, dropped);
            else inner.LogFormat(logType, context, "{0}", message);
        }

        public void LogException(Exception exception, Object context)
        {
            int dropped;
            lock (seen)
            {
                if (!Allow(exception.GetType().Name + ": " + exception.Message, LogType.Exception, out dropped)) return;
            }
            Flush();
            inner.LogException(exception, context);
            if (dropped > 0) inner.LogFormat(LogType.Error, context, "{0}: {1} [repeated {2} more times in the last 5 s]", exception.GetType().Name, exception.Message, dropped);
        }

        bool Allow(string key, LogType type, out int dropped)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now >= nextPrune) Prune(now);
            dropped = 0;
            if (seen.TryGetValue(key, out var entry))
            {
                if (now < entry.Until)
                {
                    entry.Dropped++;
                    return false;
                }
                dropped = entry.Dropped;
            }
            else seen[key] = entry = new Entry();
            entry.Until = now + WindowSeconds;
            entry.Dropped = 0;
            entry.Type = type;
            return true;
        }

        void Prune(double now)
        {
            nextPrune = now + WindowSeconds;
            foreach (var pair in seen)
            {
                if (now < pair.Value.Until) continue;
                expired.Add(pair.Key);
                if (pair.Value.Dropped > 0) flush.Add((pair.Key, pair.Value));
            }
            foreach (var key in expired) seen.Remove(key);
            expired.Clear();
        }

        void Flush()
        {
            List<(string message, Entry entry)> due = null;
            lock (seen)
            {
                if (flush.Count == 0) return;
                due = new List<(string, Entry)>(flush);
                flush.Clear();
            }
            foreach (var (message, entry) in due)
                inner.LogFormat(entry.Type == LogType.Exception ? LogType.Error : entry.Type, null, "{0} [repeated {1} more times in the last 5 s]", message, entry.Dropped);
        }
    }
}
