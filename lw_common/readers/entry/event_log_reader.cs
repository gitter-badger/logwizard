﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using log4net.Repository.Hierarchy;
using lw_common.parse;
using MultiLanguage;

namespace lw_common {
    /* inspiration

- event log -- https://msdn.microsoft.com/en-us/library/74e2ybbs%28v=vs.110%29.aspx - allow as many as the user wants...

    see about http://sanderstechnology.com/tag/net-framework-4-5/#.Vkvetb9JCHs (Group membership)

            https://msdn.microsoft.com/en-us/library/bb671200(v=vs.90).aspx#Y0


            http://michal.is/blog/query-the-event-log-with-c-net/
            http://stackoverflow.com/questions/8567368/eventlogquery-time-format-expected/8575390#8575390
            http://stackoverflow.com/questions/7966993/eventlogquery-reader-for-remote-computer
            http://stackoverflow.com/questions/12380189/eventlogquery-how-to-form-query-string

            http://codewala.net/2013/10/04/reading-event-logs-efficiently-using-c/
            http://codewala.net/2013/08/16/working-with-eventviewer-using-c/
            https://msdn.microsoft.com/en-us/library/74e2ybbs(v=vs.110).aspx




            http://www.aspheute.com/english/20000811.asp
            http://www.codeproject.com/Articles/14455/Eventlog-Viewer
            http://www.codeproject.com/Articles/91/WindowsNT-Event-Log-Viewer

    */
    public class event_log_reader : entry_text_reader_base {
        private static log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private bool fully_read_once_ = false;

        private bool logs_created_ = false;

        private const int MAX_DIFF_NEW_EVENT_MS = 2000;
        private readonly int MAX_ITEMS_PER_BLOCK = util.is_debug ? 200 : 5000;

        private class log_info {
            public bool disposed_ = false;

            public string log_type = "";

            public string remote_machine_name = "";
            public string remote_user_name = "";
            public string remote_domain = "";

            public List<EventRecord> last_events_ = new List<EventRecord>();
            public List<EventRecord> new_events_ = new List<EventRecord>();

            // if true, we've read all existing events and now we're listening for new ones
            public bool listening_for_new_events_ = false;

            // if >= 0, it's the number of total entries (we may not be able to compute this)
            public int full_log_count_ = -1;

            // how many log entries we've read so far
            public int cur_log_count_ = 0;
        }

        private List<log_info> event_logs_ = new List<log_info>();

        private string remote_password_ = "";

        public event_log_reader(settings_as_string sett) : base(sett) {
            settings.on_changed += on_settings_changed;
        }

        public string[] log_types {
            get {
                return settings.get("event.log_type", "Application|System").Split('|');
            }
        }

        public string provider_name {
            get { return settings.get("event.provider_name"); }
        } 
        public string remote_machine_name {
            get {
                return settings.get("event.remote_machine_name").Trim(); 
            }
        }
        public string remote_user_name {
            get {
                return settings.get("event.remote_user_name", ""); 
            }
        }
        public string remote_domain_name {
            get {
                return settings.get("event.remote_domain", ""); 
            }
        }

        public override string friendly_name {
            get {
                string suffix = remote_machine_name != "" ? " From [" + remote_machine_name + "]" : "";
                return util.concatenate(log_types, ", ") + suffix ;
            }
        }

        public override string progress {
            get {
                List<string> so_far = new List<string>();
                lock (this) {
                    foreach (var log in event_logs_) {
                        string name = log.log_type;
                        int len = log.full_log_count_ > 0 ? log.full_log_count_ : log.listening_for_new_events_ ? log.cur_log_count_ : -1;
                        int cur = log.cur_log_count_;
                        int processed = cur - log.last_events_.Count;
                        string now = name + " (" + processed + ", read = " + cur + (len >= 0 ? " of = " + len : "") + ")";
                        so_far.Add(now);
                    }
                }
                return "Reading " + util.concatenate(so_far, "; ");
            }
        }

        private void on_settings_changed(string name) {
            if (name == "event.remote_password") {
                if (settings.get("event.remote_password") != "") {
                    remote_password_ = settings.get("event.remote_password");
                    set_setting("event.remote_password", "");
                }
                return;
            }

            force_reload();
        }

        public override bool are_settings_complete {
            get {
                if (remote_machine_name != "")
                    return remote_password_ != "" || settings.get("event.remote_password") != "";
                return true;
            }
        }

        public override bool fully_read_once {
            get { return fully_read_once_; }
        }

        public override void force_reload() {
            base.force_reload();
            fully_read_once_ = false;
            logs_created_ = false;
            errors_.clear();
        }

        private void create_logs() {
            if (logs_created_)
                return;

            logs_created_ = true;
            lock (this) {
                foreach (var log in event_logs_)
                    log.disposed_ = true;
                event_logs_.Clear();
            }

            lock(this)
                foreach (var type in log_types) {
                    try {
                        var log = new log_info { log_type = type, remote_machine_name = remote_machine_name, remote_domain = remote_domain_name, remote_user_name = remote_user_name};
                        event_logs_.Add( log);
                        new Thread(() => read_single_log_thread(log)) {IsBackground = true }.Start();
                    } catch (Exception e) {
                        logger.Error("can't create event log " + type + "/" + remote_machine_name + " : " + e.Message);
                        errors_.add("Can't create Log " + type + " on machine " + remote_machine_name + ", Reason=" + e.Message);
                    }
                }
        }

        protected override List<log_entry_line> read_next_lines() {
            create_logs();

            lock (this) 
                fully_read_once_ = event_logs_.Count(x => x.listening_for_new_events_ && x.last_events_.Count == 0) == event_logs_.Count;

            // http://www.codeproject.com/Messages/5162204/sorting-information-from-several-threads-is-my-alg.aspx
            List< Tuple<EventRecord,string>>  next = new List<Tuple<EventRecord, string>>();
            bool needs_more_processing = true;
            lock (this) {
                // special case - a single log
                if (event_logs_.Count == 1) {
                    next = (event_logs_[0].last_events_.Count > 0 ? event_logs_[0].last_events_ : event_logs_[0].new_events_)
                        .Select(x => new Tuple<EventRecord,string>(x, event_logs_[0].log_type)).ToList() ;
                    event_logs_[0].last_events_.Clear();
                    event_logs_[0].new_events_.Clear();
                    needs_more_processing = false;
                }

                if (needs_more_processing) {
                    int listen_for_new_events = event_logs_.Count(x => x.listening_for_new_events_);
                    // note: if we're listing for new events, first process all the last ones
                    if (listen_for_new_events == event_logs_.Count && event_logs_.Count(x => x.last_events_.Count == 0) == event_logs_.Count) {
                        // we're listening for NEW EVENTS on all threads
                        List< Tuple<EventRecord,string> > all = new List<Tuple<EventRecord,string>>();
                        var now = DateTime.Now;
                        foreach (var log in event_logs_)
                            // we're waiting a bit before returning new events - just in case different threads might come up with earlier entries 
                            // (because we can't really count on any speed at all)
                            all.AddRange(log.new_events_.Where(x => x.TimeCreated.Value.AddMilliseconds(MAX_DIFF_NEW_EVENT_MS) <= now).Select(x => new Tuple<EventRecord, string>(x,log.log_type)) );
                        all = all.OrderBy(x => x.Item1.TimeCreated).ToList();
                        foreach (var log in event_logs_)
                            foreach (var entry in all)
                                log.new_events_.Remove(entry.Item1);
                        next = all;
                        needs_more_processing = false;
                    }
                }

                while (needs_more_processing) {
                    // If there is one or more threads that doesn't have at least one element, return an empty list
                    int listen_for_last_events_and_have_no_events = event_logs_.Count(x => !x.listening_for_new_events_ && x.last_events_.Count == 0);
                    if (listen_for_last_events_and_have_no_events > 0)
                        // at least one thread listening for old events and got nothing
                        break;

                    // ... the last bool -> if true, the item can be added right now ; if false, we can't add this item now
                    List < Tuple<EventRecord,string,int, bool> > last = new List<Tuple<EventRecord,string, int, bool>>();
                    for (int log_idx = 0; log_idx < event_logs_.Count; log_idx++) {
                        var log = event_logs_[log_idx];
                        if ( log.last_events_.Count > 0)
                            // if I'm listening for new events, this is ok - return this entry
                            last.Add(new Tuple<EventRecord, string,int, bool>( log.last_events_[0], log.log_type, log_idx, log.last_events_.Count > 1 || log.listening_for_new_events_));
                        else if (log.new_events_.Count > 0) 
                            last.Add(new Tuple<EventRecord, string,int, bool>( log.new_events_[0], log.log_type, log_idx, true));
                    }
                    if (last.Count < 1)
                        break;
                    var min = last.Min(x => x.Item1.TimeCreated);
                    var item = last.Find(x => x.Item1.TimeCreated == min);
                    if (!item.Item4)
                        break;

                    foreach (var log in event_logs_)
                        if (log.last_events_.Count > 0)
                            log.last_events_.Remove(item.Item1);
                        else
                            log.new_events_.Remove(item.Item1);
                    next.Add( new Tuple<EventRecord, string>(item.Item1, item.Item2) );
                    if (next.Count >= MAX_ITEMS_PER_BLOCK)
                        needs_more_processing = false;
                }
            }

            // 1.5.6t - to_log_entry can throw a lot of errors, we don't want that while being lock()ed
            return next.Select( (x) => to_log_entry(x.Item1, x.Item2) ).ToList();
        }

        private void read_single_log_thread(log_info log) {
            string query_string = "*";
            if (provider_name != "")
                query_string = "*[System/Provider/@Name=\"" + provider_name + "\"]";

            try {
                // we can read the number of entres only for local logs
                if (provider_name == "" && log.remote_machine_name == "") {
                    var dummy_log = new EventLog(log.log_type);
                    lock(this)
                        log.full_log_count_ = dummy_log.Entries.Count;
                    dummy_log.Dispose();
                }

                // waiting for user to set the password
                if ( log.remote_machine_name != "")
                    while ( remote_password_ == "")
                        Thread.Sleep(100);

                SecureString pwd = new SecureString();
                foreach ( char c in remote_password_)
                    pwd.AppendChar(c);
                EventLogSession session = log.remote_machine_name != "" ? new EventLogSession(log.remote_machine_name, remote_domain_name, remote_user_name, pwd, SessionAuthentication.Default) : null;
                pwd.Dispose();

                EventLogQuery query = new EventLogQuery(log.log_type, PathType.LogName, query_string);
                if ( session != null)
                    query.Session = session;

                EventLogReader reader = new EventLogReader(query);
                for (EventRecord rec = reader.ReadEvent(); rec != null && !log.disposed_; rec = reader.ReadEvent())
                    lock (this) {
                        log.last_events_.Add(rec);
                        ++log.cur_log_count_;
                    }

                lock (this)
                    log.listening_for_new_events_ = true;

                // at this point, listen for new events
				using (var watcher = new EventLogWatcher(query))
				{
					watcher.EventRecordWritten += (o, e) => {
                        lock(this)
                            log.new_events_.Add(e.EventRecord);
					};
					watcher.Enabled = true;

                    while ( !log.disposed_)
                        Thread.Sleep(100);
				}

            } catch (Exception e) {
                logger.Error("can't create event log " + log.log_type + "/" + remote_machine_name + " : " + e.Message);
                errors_.add("Can't create Log " + log.log_type + " on machine " + remote_machine_name + ", Reason=" + e.Message);
            }
            
        }

        private log_entry_line to_log_entry(EventRecord rec, string log_name) {
            log_entry_line entry = new log_entry_line();
            try {
                entry.add("Log", log_name);
                entry.add("EventID", "" + rec.Id);

                entry.add("level", event_level((StandardEventLevel) rec.Level));
                entry.add("date", rec.TimeCreated.Value.ToString("dd-MM-yyyy"));
                entry.add("time", rec.TimeCreated.Value.ToString("hh:mm:ss.fff"));

                try {
                    var task = rec.Task != 0 ? rec.TaskDisplayName : "";
                    entry.add("Category", task ?? "");
                } catch {
                    entry.add("Category", "");
                }

                entry.add("Machine Name", rec.MachineName);
                entry.add("Source", "" + rec.ProviderName);
                entry.add("User Name", rec.UserId != null ? rec.UserId.Value : "");
                try {
                    var keywords = rec.KeywordsDisplayNames;
                    entry.add("Keywords", keywords != null ? util.concatenate(keywords, ",") : "");
                } catch {
                    entry.add("Keywords", "");
                }

                /*
                try {
                    var desc = rec.FormatDescription();
                    entry.add("msg", desc ?? "");
                } catch {
                    entry.add("msg", "");
                }*/
                // 1.5.7+ seems FormatDescription() throws a lot of exceptions, prety sure there's a bug in .net 
                //        just make it easy
                try {
                    string desc = util.concatenate( rec.Properties.Select(x => x.Value.ToString()), "\r\n");
                    entry.add("msg", desc);
                } catch {
                    entry.add("msg", "");
                }


            } catch (Exception e) {
                logger.Fatal("can't convert EventRectord to entry " + e.Message);
            }
            return entry;
        }


        private static string event_level(StandardEventLevel type) {
            switch (type) {
            case StandardEventLevel.LogAlways:
                return "DEBUG";
            case StandardEventLevel.Critical:
                return "INFO";
            case StandardEventLevel.Error:
                return "ERROR";
            case StandardEventLevel.Warning:
                return "WARN";
            case StandardEventLevel.Informational:
                return "INFO";
            case StandardEventLevel.Verbose:
                return "VERB";
            default:
                return "";
            }
        }

    }
}
