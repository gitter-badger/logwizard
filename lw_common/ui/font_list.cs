﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;

namespace lw_common.ui {
    class font_list {
        private Dictionary<string, Font> fonts_ = new Dictionary<string, Font>();

        public Font get_font(Font f, bool bold, bool italic, bool underline) {
            string id = font_to_string(f, bold, italic, underline);
            if (!fonts_.ContainsKey(id))
                fonts_.Add(id, create_new(f.Name, (int)(f.Size + .5), bold, italic, underline));
            return fonts_[id];
        }

        public Font get_font(string font_name, int size, bool bold, bool italic, bool underline) {
            Debug.Assert(font_name != "");
            string id = font_to_string(font_name, size, bold, italic, underline);
            if (!fonts_.ContainsKey(id))
                fonts_.Add(id, create_new(font_name, size, bold, italic, underline));
            return fonts_[id];
        }

        private string font_to_string(Font f) {
            return font_to_string(f.Name, (int) (f.Size + .5), f.Bold, f.Italic, f.Underline);
        }
        private string font_to_string(Font f, bool bold, bool italic, bool underline) {
            return font_to_string(f.Name, (int) (f.Size + .5), bold, italic, underline);
        }
        private string font_to_string(string font_name,int size, bool bold, bool italic, bool underline) {
            return font_name + "|" + size + "|" + bold + "|" + italic + "|" + underline;
        }

        private Font create_new(string font_name, int size, bool bold, bool italic, bool underline) {
            FontStyle style = FontStyle.Regular;
            if (bold)
                style = style | FontStyle.Bold;
            if (italic)
                style = style | FontStyle.Italic;
            if (underline)
                style = style | FontStyle.Underline;
            return new Font(font_name, size, style);
        }
    }
}
