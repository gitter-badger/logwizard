﻿/* 
 * Copyright (C) 2014-2015 John Torjo
 *
 * This file is part of LogWizard
 *
 * LogWizard is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * LogWizard is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *
 * If you wish to use this code in a closed source application, please contact john.code@torjo.com 
 *
 * **** Get Latest version at https://github.com/jtorjo/logwizard **** 
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms.VisualStyles;

namespace lw_common.parse.parsers {
    internal abstract class log_parser_base : IDisposable {
        protected bool disposed_ = false;

        protected readonly settings_as_string_readonly sett_;
        private aliases aliases_;

        private List<string> column_names_ = new List<string>(); 

        protected log_parser_base(settings_as_string_readonly sett) {
            sett_ = sett;
            sett_.on_changed += on_settings_changed;
            on_updated_settings();            
        }

        public abstract void read_to_end();

        public abstract int line_count { get; }

        public abstract line line_at(int idx);

        public abstract void force_reload();

        public abstract bool up_to_date { get; }

        // column names - parsed from the log (if any)
        public List<string> column_names {
            get { lock(this) return column_names_; }
            internal set {
                lock(this)
                    column_names_ = value;
                if ( column_names_.Count > 0)
                    aliases_.on_column_names(column_names_);
            }
        }

        public aliases aliases {
            get { return aliases_; }
        }

        private void on_settings_changed(string name) {
            if (name == "name")
                // this is the friendly name assigned to this reader
                return;
            on_updated_settings();
        }

        protected virtual void on_updated_settings() {
            aliases_ = new aliases(sett_.get("aliases"));
            if ( column_names_.Count > 0)
                aliases_.on_column_names(column_names_);
        }

        internal settings_as_string_readonly settings {
            get { return sett_; }
        }

        public void Dispose() {
            disposed_ = true;
        }

    }
}
