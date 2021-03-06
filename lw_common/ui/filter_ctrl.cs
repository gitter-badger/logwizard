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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using BrightIdeasSoftware;
using LogWizard;

namespace lw_common.ui {
    // 1.0.91+
    // note: this is fully synchronized with current view (ui_view) - if you need access to the items, you can access the view directly
    public partial class filter_ctrl : UserControl {
        private static log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // sometimes the first refresh fails - do another one
        private readonly int SECOND_REFRESH_MS = 100; //util.is_debug ? 1000 : 200;

        private ui_view view_;
        private int view_idx_ = -1;
        private int ignore_change_ = 0;

        private bool needs_save_ = false;

        public delegate void void_func();
        public delegate void idx_func(int filter_idx);
        public delegate void view_idx_func(int view_idx);

        public void_func on_save;
        public view_idx_func ui_to_view;
        // ... this means the view has changed drastically, and filters need to be re-run
        public view_idx_func on_rerun_view;
        // ... this means the UI of the view needs refresh
        public view_idx_func on_refresh_view;

        public idx_func mark_match;

        public bool design_mode = true;

        private string before_ctrl_enter_ = null;


        class filter_item {
            private ui_filter filter_;
            public filter_item(ui_filter filter) {
                filter_ = filter;
            }

            public bool enabled {
                get { return filter_.enabled; }
                set { filter_.enabled = value; }
            }

            public bool dimmed {
                get { return filter_.dimmed; }
                set { filter_.dimmed = value; }
            }

            public string text {
                get { return filter_.text; }
                set { filter_.text = value; }
            }

            public bool apply_to_existing_lines {
                get { return filter_.apply_to_existing_lines; }
                set { filter_.apply_to_existing_lines = value; }
            }

            public string filter_id {
                get {
                    raw_filter_row row = new raw_filter_row(filter_.text, filter_.apply_to_existing_lines);
                    return row.filter_id;
                }
            }
            public string unique_id {
                get {
                    raw_filter_row row = new raw_filter_row(filter_.text, filter_.apply_to_existing_lines);
                    return row.unique_id;
                }
            }

            public string found_count = "";

            // "name" is just a friendly name for the text
            public string name {
                get {
                    string[] lines = text.Split(new string[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries);
                    var filter_name = lines.FirstOrDefault(l => l.Trim().StartsWith("## "));
                    if (filter_name != null)
                        return filter_name.Trim().Substring(3).Trim();

                    return util.concatenate(lines, " | ");
                }
            }
        }

        public filter_ctrl() {
            InitializeComponent();
        }

        public void undo() {
            // FIXME
        }

        // the controls we can navigate through with TAB hotkey
        public List<Control> tab_navigatable_controls {
            get {
                var nav = new List<Control>();
                nav.Add(filterCtrl);
                if ( curFilterCtrl.Enabled)
                    nav.Add(curFilterCtrl);
                return nav;
            }
        }

        public bool is_editing_any_filter {
            get { return win32.focused_ctrl() == curFilterCtrl; }
        }

        public bool is_focus_on_filter_list {
            get { return win32.focused_ctrl() == filterCtrl; }
        }

        public bool can_handle_toggle_enable_dimmed_now {
            get { return is_focus_on_filter_list; }
        }

        public int sel {
            get { return filterCtrl.SelectedIndex;  }
        }

        public List<raw_filter_row> to_filter_row_list() {
            List<raw_filter_row> lvf = new List<raw_filter_row>();
            int count = filterCtrl.GetItemCount();
            for ( int idx = 0; idx < count; ++idx) {
                filter_item i = filterCtrl.GetItem(idx).RowObject as filter_item;
                raw_filter_row filt = new raw_filter_row(i.text, i.apply_to_existing_lines);
                filt.enabled = i.enabled;
                filt.dimmed = i.dimmed;

                if ( filt.is_valid)
                    lvf.Add(filt);
            }
            return lvf;
        } 

        public void new_row_count(int filter_idx, int count) {
            Debug.Assert( filter_idx < view_.filters.Count);
            var i = filterCtrl.GetItem(filter_idx).RowObject as filter_item;
            i.found_count = count > 0 ? "" + count : "";

            ++ignore_change_;
            filterCtrl.RefreshObject(i);
            --ignore_change_;
        }

        public void toggle_enabled_dimmed() {
            int sel = filterCtrl.SelectedIndex;
            if (sel >= 0) {
                var filt = view_.filters[sel];
                bool enabled, dimmed;
                if (filt.enabled && !filt.dimmed) {
                    enabled = false;
                    dimmed = false;
                }
                else if (!filt.enabled && !filt.dimmed) {
                    enabled = false;
                    dimmed = true;
                }
                else if (!filt.enabled && filt.dimmed) {
                    enabled = true;
                    dimmed = false;
                } else {
                    enabled = false;
                    dimmed = false;
                }

                filt.enabled = enabled;
                filt.dimmed = dimmed;

                var i = filterCtrl.GetItem(sel).RowObject as filter_item;
                filterCtrl.RefreshObject(i);

                on_save();
                ui_to_view(view_idx_);
                on_rerun_view(view_idx_);
            }
        }


        public void view_to_ui(ui_view view, int view_idx) {
            if ( view_ != view && view_ != null)
                if (needs_save_) {
                    on_save();
                    ui_to_view(view_idx_);
                }

            view_ = view;
            view_idx_ = view_idx;

            ++ignore_change_;
            List<object> items = new List<object>();
            var filters = view_.filters;
            for (int idx = 0; idx < filters.Count; ++idx) {
                var i = new filter_item(filters[idx]);
                items.Add(i);
            }
            filterCtrl.SetObjects(items);

            curFilterCtrl.Text = "";
            applyToExistingLines.Checked = false;
            curFilterCtrl.Enabled = filterCtrl.SelectedIndex >= 0;
            --ignore_change_;

            ui_to_view(view_idx_);
        }


        private void filterCtrl_SelectedIndexChanged(object sender, EventArgs e) {
            int sel_filter = filterCtrl.SelectedIndex;
            bool has_sel = sel_filter >= 0;
            if (!has_sel) {
                ++ignore_change_;
                curFilterCtrl.Text = "";
                applyToExistingLines.Checked = false;
                --ignore_change_;
                mark_match(-1);
                return;
            }

            filter_item i = filterCtrl.GetItem(sel_filter).RowObject as filter_item;
            ++ignore_change_;
            applyToExistingLines.Checked = i.apply_to_existing_lines;
            --ignore_change_;

            raw_filter_row filt = new raw_filter_row(i.text, i.apply_to_existing_lines);
            if (filt.is_valid) {
                /*
                var lv = ensure_we_have_log_view_for_tab(sel_view);
                Color fg = util.str_to_color(sett.get("filter_fg", "transparent"));
                Color bg = util.str_to_color(sett.get("filter_bg", "#faebd7"));
                lv.mark_match(sel_filter, fg, bg);
                */
                mark_match(sel_filter);
            }
        }

        private void filterCtrl_KeyPress(object sender, KeyPressEventArgs e) {
            e.Handled = true;
        }

        private void filterCtrl_MouseDown(object sender, MouseEventArgs e) {
            // for some very fucked up strange reason, if FullRowSelect is on, "on mouse up" doesn't get called - simulating a mouse move will trigger it
            // ... note: there's a bug when clicking on a combo or on a checkbox, and then clicking on the same type of control on another row
            var mouse = win32.GetMousePos();
            win32.SetMousePos( mouse.x+1, mouse.y);

            if ( (e.Button & MouseButtons.Right) == MouseButtons.Right)
                filterContextMenu.Show(Cursor.Position);

        }

        private void filterCtrl_CellEditStarting(object sender, BrightIdeasSoftware.CellEditEventArgs e) {
            if (e.SubItemIndex == filterCol.Index) {
                e.Cancel = true;

                var sel = filterCtrl.SelectedObject as filter_item;
                // we must be editing a filter row!
                Debug.Assert(sel != null);

                util.postpone(() => {
                    curFilterCtrl.Focus();
                    curFilterCtrl.SelectionStart = curFilterCtrl.TextLength;                    
                }, 10);
            }
        }

        private void filterCtrl_ItemsChanged(object sender, BrightIdeasSoftware.ItemsChangedEventArgs e) {
            if (win32.focused_ctrl() == curFilterCtrl)
                return;
            if (ignore_change_ > 0)
                return;

            on_save();
        }

        private void filterCtrl_SelectionChanged(object sender, EventArgs e) {
            ++ignore_change_;
            var sel = filterCtrl.SelectedObject as filter_item;
            curFilterCtrl.Text = sel != null ? sel.text : "";
            curFilterCtrl.Enabled = sel != null ;
            if (filterCtrl.SelectedIndices.Count > 1)
                // in this case, it's multi-sel
                curFilterCtrl.Enabled = false;
            if (sel != null) {
                var row = new raw_filter_row(sel.text, sel.apply_to_existing_lines);
                filterLabel.BackColor = row.bg;
                filterLabel.ForeColor = row.fg;
            } else {
                filterLabel.BackColor = DefaultBackColor;
                filterLabel.ForeColor = Color.White;
            }
            --ignore_change_;
        }

        private void curFilterCtrl_TextChanged(object sender, EventArgs e) {
            if (before_ctrl_enter_ != null) {
                ++ignore_change_;
                string before = before_ctrl_enter_;
                before_ctrl_enter_ = null;
                curFilterCtrl.Text = before;
                curFilterCtrl.SelectionStart = curFilterCtrl.TextLength;
                --ignore_change_;
                return;
            }

            if (ignore_change_ > 0)
                return;

            if ( filterCtrl.GetItemCount() == 0) {
                // this will in turn call us again
                addFilter_Click(null,null);
                return;
            }
            var sel = filterCtrl.SelectedObject as filter_item;
            // we must be editing a filter row!
            Debug.Assert(sel != null);
            if (sel == null) 
                return;

            if (sel.text != curFilterCtrl.Text) {
                sel.text = curFilterCtrl.Text;
                filterCtrl.RefreshObject(sel);

                needs_save_ = true;
            }

            var row = new raw_filter_row(curFilterCtrl.Text, applyToExistingLines.Checked);
            bool is_valid = row.is_valid;
            Color bg = is_valid ? Color.White : Color.LightPink;
            if (curFilterCtrl.BackColor.ToArgb() != bg.ToArgb())
                curFilterCtrl.BackColor = bg;

            if (row.is_valid) {
                if (filterLabel.BackColor.ToArgb() != row.bg.ToArgb())
                    filterLabel.BackColor = row.bg;
                if (filterLabel.ForeColor.ToArgb() != row.fg.ToArgb())
                    filterLabel.ForeColor = row.fg;
            }
        }

        private void applyToExistingLines_CheckedChanged(object sender, EventArgs e) {
            if (ignore_change_ > 0)
                return;
            if (filterCtrl.GetItemCount() == 0)
                return;

            var sel = filterCtrl.SelectedObject as filter_item;
            // we must be editing a filter row!
            Debug.Assert(sel != null);
            if (sel == null) 
                return;

            if (sel.apply_to_existing_lines != applyToExistingLines.Checked) {
                sel.apply_to_existing_lines = applyToExistingLines.Checked;
                filterCtrl.RefreshObject(sel);
                needs_save_ = true;
                on_save();
            }
        }

        private void addFilter_Click(object sender, EventArgs e) {
            if (view_ == null) {
                Debug.Assert(false);
                return;
            }

            ui_filter new_ui = new ui_filter {enabled = true, dimmed = false, text = "", apply_to_existing_lines = false};
            filter_item new_ = new filter_item( new_ui);

            view_.filters.Add( new_ui );

            ++ignore_change_;
            filterCtrl.AddObject(new_);
            filterCtrl.SelectedIndices.Clear();
            filterCtrl.SelectObject(new_);
            curFilterCtrl.Text = "";
            --ignore_change_;

            on_save();

            util.postpone(() => {
                curFilterCtrl.Focus();
                curFilterCtrl.SelectionStart = curFilterCtrl.TextLength;                    
            }, 10);
        }

        private void delFilter_Click(object sender, EventArgs e) {
            if (view_ == null) {
                Debug.Assert(false);
                return;
            }

            var sel = filterCtrl.SelectedObject as filter_item;
            if ( sel != null) {
                ++ignore_change_;
                int idx = filterCtrl.SelectedIndex;
                view_.filters.RemoveAt(idx);
                filterCtrl.RemoveObject(sel);

                int new_sel = view_.filters.Count > idx ? idx : view_.filters.Count > 0 ? view_.filters.Count - 1 : -1;
                if (new_sel >= 0)
                    filterCtrl.SelectedIndex = new_sel;
                --ignore_change_;
            }

            on_save();
        }

        private void tipsHotkeys_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            Process.Start("https://github.com/jtorjo/logwizard/wiki/Filters");
        }

        private void viewToClipboard_Click(object sender, EventArgs e) {
            if (view_ == null)
                return;

            if (view_.filters.Count < 1)
                return; // nothing to copy
            var formatter = new XmlSerializer( typeof(ui_view));
            string to_copy = "";
            using (var stream = new MemoryStream()) {
                formatter.Serialize(stream, view_);
                stream.Flush();
                stream.Position = 0;
                using (var reader = new StreamReader(stream))
                    to_copy = reader.ReadToEnd();
            }
            Clipboard.SetText(to_copy);                
        }

        private void viewFromClipboard_Click(object sender, EventArgs ea) {
            if (view_ == null)
                return;

            try {
                string txt = Clipboard.GetText();
                var formatter = new XmlSerializer( typeof(ui_view));
                using (var stream = new MemoryStream()) {
                    using (var writer = new StreamWriter(stream)) {
                        writer.Write(txt);
                        writer.Flush();
                        stream.Position = 0;
                        using (var reader = new StreamReader(stream)) {
                            var new_view = (ui_view) formatter.Deserialize(reader);
                            // we don't care about the name, just the filters
                            new_view.filters.ForEach(f => f.text = util.normalize_deserialized_enters(f.text));
                            view_.filters = new_view.filters;
                        }
                    }
                }
                ui_to_view(view_idx_);
                on_save();
                on_rerun_view(view_idx_);
            } catch(Exception e) {
                logger.Error("can't copy from clipboard: " + e.Message);
                util.beep(util.beep_type.err);
            }
        }

        private void selectColor_Click(object sender, EventArgs e) {
            if (filterCtrl.SelectedIndex < 0)
                return; // there's nothing selected

            var sel = new select_color_form();
            if (sel.ShowDialog() == DialogResult.OK) {
                string sel_color = util.color_to_str( sel.SelectedColor);

                var lines = curFilterCtrl.Text.Split( new string[] { "\r\n" }, StringSplitOptions.None ).ToList();
                int sel_start = curFilterCtrl.SelectionStart;
                int edited_line = util.index_to_line(curFilterCtrl.Text, sel_start);
                if (edited_line >= 0 && !filter_line.is_color_or_font_line(lines[edited_line]))
                    // user is editing a line that is not a color line
                    edited_line = -1;
                if (edited_line == -1) {
                    // it's not with the cursor on a line - find the first line that would actually be a color
                    for ( int i = 0; i < lines.Count && edited_line == -1; ++i)
                        if (filter_line.is_color_or_font_line( lines[i]))
                            edited_line = i;
                }
                if (edited_line != -1) {
                    // in this case, he's editing the color from a given line
                    bool is_color_line = filter_line.is_color_or_font_line( lines[edited_line]);
                    bool is_replacing = lines[edited_line].Split(new string[] {" "}, StringSplitOptions.RemoveEmptyEntries).Length <= 2 &&
                                        lines[edited_line].TrimEnd() == lines[edited_line];
                    if (is_color_line) {
                        if (is_replacing)
                            lines[edited_line] = (lines[edited_line].Trim().StartsWith("color ") ? "color" : "match_color") + " " + sel_color;
                        else
                            lines[edited_line] += " " + sel_color;
                    } else
                        // the edited line does not contain any color, thus, we append the color line
                        edited_line = -1;
                }


                if (edited_line == -1) {
                    lines.Add("color " + sel_color);
                    sel_start = -1;
                }


                curFilterCtrl.Text = util.concatenate(lines, "\r\n");
                if ( sel_start >= 0 && sel_start < curFilterCtrl.TextLength)
                    curFilterCtrl.SelectionStart = sel_start;


                ui_to_view(view_idx_);
                on_save();
                do_refresh();
            }

        }


        private void filter_ctrl_Load(object sender, EventArgs e) {
            // doesn't work
            //if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
              //  return;

            if (!design_mode)
                Debug.Assert(on_save != null && on_rerun_view != null && on_refresh_view != null && ui_to_view != null && mark_match != null);
        }

        public void select_filter(string name) {
            for ( int idx = 0; idx < filterCtrl.GetItemCount(); ++idx) {
                var i = filterCtrl.GetItem(idx).RowObject as filter_item;
                if ( i.name == name)
                    filterCtrl.SelectedIndex = idx;
            }
        }

        public void select_filter_rows(List<int> filter_row_indexes) {
            Debug.Assert(filter_row_indexes.Count > 0);
            if (filter_row_indexes.Count == 1)
                filterCtrl.SelectedIndex = filter_row_indexes[0];
            else {
                ++ignore_change_;
                
                filterCtrl.SelectedIndex = filter_row_indexes[0];

                filterCtrl.SelectedIndices.Clear();
                foreach (int idx in filter_row_indexes)
                    filterCtrl.SelectedIndices.Add(idx);
                curFilterCtrl.Enabled = false;
                filterCtrl.EnsureVisible(filter_row_indexes[0]);

                --ignore_change_;
            }
        }


        private void moveUpToolStripMenuItem_Click(object sender, EventArgs e) {
            int sel = filterCtrl.SelectedIndex;
            if (sel < 1)
                return;
            var filters = view_.filters;
            var cur = filters[sel];
            filters.RemoveAt(sel);
            filters.Insert(sel - 1, cur);

            ++ignore_change_;
            view_to_ui(view_, view_idx_);
            --ignore_change_;
            filterCtrl.SelectedIndex = sel - 1;

            // note: we will re-run the view once we leave the filter control - we don't want to do this too fast, since 
            //       that could interfere with editing the filter - we don't want that
        }
        private void moveToTopToolStripMenuItem_Click(object sender, EventArgs e) {
            int sel = filterCtrl.SelectedIndex;
            if (sel < 1)
                return;
            var filters = view_.filters;
            var cur = filters[sel];
            filters.RemoveAt(sel);
            filters.Insert(0, cur);

            ++ignore_change_;
            view_to_ui(view_, view_idx_);
            --ignore_change_;
            filterCtrl.SelectedIndex = 0;

            // note: we will re-run the view once we leave the filter control - we don't want to do this too fast, since 
            //       that could interfere with editing the filter - we don't want that
        }

        private void moveDownToolStripMenuItem_Click(object sender, EventArgs e) {
            int sel = filterCtrl.SelectedIndex;
            if (sel < 0)
                return;
            if (sel == filterCtrl.GetItemCount() - 1)
                return;
            var filters = view_.filters;
            var cur = filters[sel];
            filters.RemoveAt(sel);
            filters.Insert(sel + 1, cur);

            ++ignore_change_;
            view_to_ui(view_, view_idx_);
            --ignore_change_;
            filterCtrl.SelectedIndex = sel + 1;

            // note: we will re-run the view once we leave the filter control - we don't want to do this too fast, since 
            //       that could interfere with editing the filter - we don't want that
        }
        private void moveToBottomToolStripMenuItem_Click(object sender, EventArgs e) {
            int sel = filterCtrl.SelectedIndex;
            if (sel < 0)
                return;
            if (sel == filterCtrl.GetItemCount() - 1)
                return;
            var filters = view_.filters;
            var cur = filters[sel];
            filters.RemoveAt(sel);
            filters.Add(cur);

            ++ignore_change_;
            view_to_ui(view_, view_idx_);
            --ignore_change_;
            filterCtrl.SelectedIndex = filterCtrl.GetItemCount() - 1;
            
            // note: we will re-run the view once we leave the filter control - we don't want to do this too fast, since 
            //       that could interfere with editing the filter - we don't want that
        }

        private void save_if_user_left() {
            if (needs_save_) {
                var focus = win32.focused_ctrl();
                bool is_ours = focus == curFilterCtrl || focus == applyToExistingLines || focus == filterCtrl || focus == addFilter || focus == delFilter;
                if (!is_ours) {
                    needs_save_ = false;
                    on_save();
                    ui_to_view(view_idx_);
                    curFilterCtrl.Enabled = filterCtrl.SelectedIndex >= 0;
                }
            }
        }

        private void curFilterCtrl_Leave(object sender, EventArgs e) {
            if (needs_save_) 
                util.postpone(save_if_user_left, 10);
        }


        private void filter_ctrl_SizeChanged(object sender, EventArgs e) {
            logger.Info("filter pane =" + Width + " x" + Height);
        }

        private void applyToExistingLines_Leave(object sender, EventArgs e) {
            if (needs_save_) 
                util.postpone(save_if_user_left, 10);
        }

        private void filterCtrl_Leave(object sender, EventArgs e) {
            if (needs_save_) 
                util.postpone(save_if_user_left, 10);
        }

        private void addFilter_Leave(object sender, EventArgs e) {
            if (needs_save_) 
                util.postpone(save_if_user_left, 10);
        }

        private void delFilter_Leave(object sender, EventArgs e) {
            if (needs_save_) 
                util.postpone(save_if_user_left, 10);
        }

        private void curFilterCtrl_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e) {
            string s = util.key_to_action(e);
            if (s == "ctrl-return")
                e.IsInputKey = true;
        }

        private void curFilterCtrl_KeyDown(object sender, KeyEventArgs e) {
            string s = util.key_to_action(e);
            if (s == "ctrl-return") {
                before_ctrl_enter_ = curFilterCtrl.Text;
                e.Handled = true;
                ui_to_view(view_idx_);
                on_save();
                do_refresh();
            }

        }

        private void filterCtrl_MouseClick(object sender, MouseEventArgs e) {
        }

        private void filterCtrl_Click(object sender, EventArgs e) {

        }

        private void filterCtrl_CellClick(object sender, BrightIdeasSoftware.CellClickEventArgs e) {
            
        }


        public void update_filter_row(string id, string filter_str, bool apply_to_existing_lines) {
            bool updated = false;
            bool was_selected = false;
            string unique_id = new raw_filter_row(filter_str, apply_to_existing_lines).unique_id;
            for (int idx = 0; idx < filterCtrl.GetItemCount(); ++idx) {
                var i = filterCtrl.GetItem(idx).RowObject as filter_item;
                bool is_same = i.filter_id == id;
                if ( !is_same)
                    // in this case, check if we have an existing filter exactly the same, but with a different color
                    is_same = unique_id == i.unique_id;
                if (is_same) {
                    i.text = raw_filter_row.merge_lines(i.text, filter_str);
                    was_selected = idx == filterCtrl.SelectedIndex;
                    filterCtrl.RefreshObject(i);
                    updated = true;
                    break;
                }
            }

            if (!updated) {
                // new filter
                ui_filter new_ui = new ui_filter {enabled = true, dimmed = false, text = filter_str, apply_to_existing_lines = apply_to_existing_lines};
                filter_item new_ = new filter_item( new_ui);

                view_.filters.Add( new_ui );

                ++ignore_change_;
                filterCtrl.AddObject(new_);
                --ignore_change_;
            }

            // if editing, need to set_aliases that as well
            if (was_selected) {
                ++ignore_change_;
                curFilterCtrl.Text = filter_str;
                --ignore_change_;                
            }

            on_save();
            ui_to_view(view_idx_);
            if (updated)
                do_refresh();
            else
                on_rerun_view(view_idx_);

        }

        private void do_refresh() {
            update_colors();

            on_refresh_view(view_idx_);
            util.postpone(() => on_refresh_view(view_idx_), SECOND_REFRESH_MS);
        }


        // goes to select the filter created here
        public void edit_filter_row_by_filter_id(string id) {
            for (int idx = 0; idx < filterCtrl.GetItemCount(); ++idx)
                if ((filterCtrl.GetItem(idx).RowObject as filter_item).filter_id == id) {
                    edit_filter_row(idx);
                    break;
                }
        }


        public void edit_filter_row(int filter_row_idx) {
            ++ignore_change_;
            filterCtrl.SelectedIndex = filter_row_idx;
            --ignore_change_;

            // wait a bit for the UI to set_aliases
            util.postpone(() => {                
                curFilterCtrl.SelectionStart = curFilterCtrl.TextLength;
                curFilterCtrl.Focus();
            }, 150);
        }

        public void update_colors() {
            for (int i = 0; i < filterCtrl.GetItemCount(); ++i) {
                update_color(filterCtrl.GetItem(i));
                filterCtrl.RefreshObject( filterCtrl.GetItem(i).RowObject );
            }
        }

        private void update_color(OLVListItem olv_row) {
            Color bg, fg;
            if (!app.inst.show_filter_row_in_filter_color) {
                fg = app.inst.fg;
                bg = app.inst.bg;
            } else {
                filter_item i = olv_row.RowObject as filter_item;
                var row = new raw_filter_row(i.text, i.apply_to_existing_lines);
                fg = row.fg;
                bg = row.bg;

                if (fg == util.transparent)
                    fg = row.match_fg;
                if (bg == util.transparent)
                    bg = row.match_bg;

                if (fg == util.transparent)
                    fg = app.inst.fg;
                if (bg == util.transparent)
                    bg = app.inst.bg;
            }

            if (olv_row.BackColor.ToArgb() != bg.ToArgb())
                olv_row.BackColor = bg;

            if (olv_row.ForeColor.ToArgb() != fg.ToArgb())
                olv_row.ForeColor = fg;
        }

        private void filterCtrl_FormatRow(object sender, BrightIdeasSoftware.FormatRowEventArgs e) {
            update_color( e.Item);
        }

    }
}
