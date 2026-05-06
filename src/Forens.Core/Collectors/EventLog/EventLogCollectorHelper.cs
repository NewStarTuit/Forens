using System;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using Forens.Core.Collection;

namespace Forens.Core.Collectors.EventLog
{
    public static class EventLogCollectorHelper
    {
        public static void Collect(
            string channelName,
            string outputFileName,
            CollectionContext ctx,
            ISourceWriter writer)
        {
            string xpath = BuildXPath(ctx.TimeFrom, ctx.TimeTo);
            EventLogQuery query;
            try
            {
                query = new EventLogQuery(channelName, PathType.LogName, xpath)
                {
                    ReverseDirection = false
                };
            }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "Failed to build EventLogQuery for {Channel}", channelName);
                writer.RecordPartial("Failed to build event log query: " + ex.Message);
                return;
            }

            using (var jl = writer.OpenJsonlFile(outputFileName))
            {
                EventLogReader reader;
                try { reader = new EventLogReader(query); }
                catch (UnauthorizedAccessException ex)
                {
                    ctx.Logger.Warning(ex, "Access denied reading channel {Channel}", channelName);
                    writer.RecordPartial("Access denied reading event log channel " + channelName);
                    return;
                }
                catch (EventLogNotFoundException ex)
                {
                    ctx.Logger.Verbose(ex, "Channel not found: {Channel}", channelName);
                    writer.RecordPartial("Event log channel not found: " + channelName);
                    return;
                }

                using (reader)
                {
                    EventRecord ev;
                    while (true)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        try { ev = reader.ReadEvent(); }
                        catch (EventLogException ex)
                        {
                            ctx.Logger.Warning(ex, "EventLogReader.ReadEvent failed on {Channel}", channelName);
                            writer.RecordPartial("EventLogReader.ReadEvent failed: " + ex.Message);
                            break;
                        }
                        if (ev == null) break;

                        using (ev)
                        {
                            try
                            {
                                jl.Write(ToRecord(ev));
                                writer.RecordItem();
                            }
                            catch (Exception ex)
                            {
                                ctx.Logger.Verbose(ex, "Skipped one event due to render error");
                                writer.RecordPartial("One or more events could not be rendered");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Probes the named channel cheaply to decide whether it can be read.
        /// Returns Skip(NotAvailable) if the channel does not exist, Skip(RequiresElevation)
        /// if access is denied, Ok otherwise.
        /// </summary>
        public static SourcePrecondition Preflight(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "channel name not configured");
            try
            {
                using (var session = new EventLogSession())
                {
                    session.GetLogInformation(channelName, PathType.LogName);
                    return SourcePrecondition.Ok();
                }
            }
            catch (EventLogNotFoundException)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Event log channel not found: " + channelName);
            }
            catch (UnauthorizedAccessException)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Access denied reading event log channel: " + channelName);
            }
            catch (Exception ex)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Cannot access event log channel " + channelName + ": " + ex.Message);
            }
        }

        public static string BuildXPath(DateTimeOffset? from, DateTimeOffset? to)
        {
            if (!from.HasValue && !to.HasValue) return "*";

            string fromIso = from?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            string toIso = to?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            if (from.HasValue && to.HasValue)
                return string.Format("*[System[TimeCreated[@SystemTime>='{0}' and @SystemTime<='{1}']]]", fromIso, toIso);
            if (from.HasValue)
                return string.Format("*[System[TimeCreated[@SystemTime>='{0}']]]", fromIso);
            return string.Format("*[System[TimeCreated[@SystemTime<='{0}']]]", toIso);
        }

        private static EventLogJsonRecord ToRecord(EventRecord ev)
        {
            string message = null;
            try { message = ev.FormatDescription(); } catch { }

            string xml = null;
            try { xml = ev.ToXml(); } catch { }

            return new EventLogJsonRecord
            {
                RecordId = ev.RecordId,
                EventId = ev.Id,
                Level = ev.Level,
                LevelName = ev.LevelDisplayName,
                Provider = ev.ProviderName,
                Channel = ev.LogName,
                TaskCategory = ev.TaskDisplayName,
                Opcode = ev.OpcodeDisplayName,
                Computer = ev.MachineName,
                UserSid = ev.UserId?.Value,
                TimeCreatedUtc = ev.TimeCreated.HasValue
                    ? new DateTimeOffset(ev.TimeCreated.Value.ToUniversalTime(), TimeSpan.Zero)
                    : (DateTimeOffset?)null,
                ProcessId = ev.ProcessId,
                ThreadId = ev.ThreadId,
                Message = message,
                Xml = xml
            };
        }

        private sealed class EventLogJsonRecord
        {
            public long? RecordId { get; set; }
            public int EventId { get; set; }
            public byte? Level { get; set; }
            public string LevelName { get; set; }
            public string Provider { get; set; }
            public string Channel { get; set; }
            public string TaskCategory { get; set; }
            public string Opcode { get; set; }
            public string Computer { get; set; }
            public string UserSid { get; set; }
            public DateTimeOffset? TimeCreatedUtc { get; set; }
            public int? ProcessId { get; set; }
            public int? ThreadId { get; set; }
            public string Message { get; set; }
            public string Xml { get; set; }
        }
    }
}
