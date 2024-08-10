using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Wolfire
{
    public class DebugText : MonoBehaviour
    {
        [Flags]
        public enum DebugTextCategory
        {
            None = 0,
            Default = 1,
            Tile = 2,
            Audio = 4,
            Weapon = 8,
            Enemy = 16,
            Profile = 32,
            Perf = 64,
            Event = 128,
            Stats = 256,
            Spawn = 512,
            All = ~0
        }

        public const string default_color = "<color=#ffffffa0>";
        private static DebugText instance;
        public bool imgui_enabled = true;
        public bool in_world_overlay_enabled = true;
        private readonly StringBuilder display_text = new("", 4096);
        private Dictionary<string, Entry> backup_entries = new();
        private Dictionary<string, Entry> entries = new();

        private void Start()
        {
            instance = this;
        }

        private void Update()
        {
            backup_entries.Clear();
            display_text.Length = 0;
            foreach (var pair in entries)
            {
                var entry = pair.Value;
                if (entry.end_time >= Time.time)
                {
                    display_text.AppendFormat("{0}{1}\n", default_color, entry.display_text);
                    backup_entries.Add(pair.Key, pair.Value);
                }
            }

            var temp = entries;
            entries = backup_entries;
            backup_entries = temp;
            /*
                ImGui.SetNextWindowSize(new Vector2(500, 400), ImGuiCond.FirstUseEver);
                if(ImGui.Begin("Debug Text")) {
                    foreach (KeyValuePair<string, Entry> pair in entries) {
                        Entry entry = pair.Value;
                        if (entry.end_time >= Time.time) {
                            ImGui.TextUnformatted(pair.Value.display_text);
                        }
                    }
                }
                ImGui.End();  */
        }

        public static DebugText Instance()
        {
            return instance;
        }

        public static void Add(string key, string value, float fade_time = 0.0f,
            DebugTextCategory debug_text_category = DebugTextCategory.Default)
        {
            if (instance != null)
            {
                if (!instance.entries.TryGetValue(key, out var entry))
                {
                    entry = new Entry();
                    instance.entries.Add(key, entry);
                }

                entry.display_text = value;
                entry.end_time = Time.time + fade_time;
                entry.category = debug_text_category;
            }
        }

        // Shortcut to just add debug text with same key and value
        public static void Add(string key, float fade_time = 0.0f,
            DebugTextCategory debug_text_category = DebugTextCategory.Default)
        {
            Add(key, key, fade_time, debug_text_category);
        }

        // Shortcut to just display a variable like "varname: varvalue"
        // Warning: This method calls string.Format internally, so you may want to only conditionally call this -
        //          if(DebugText.IsDebugWindowVisible && DebugText.CategoryVisible(DebugText.DebugTextCategory.Default)) {
        //              // ... DebugText.AddVar(
        //          }
        public static void AddVar(string name, object obj, float fade_time,
            DebugTextCategory debug_text_category = DebugTextCategory.Default)
        {
            Add(name, string.Format("{0}: {1}", name, obj), fade_time, debug_text_category);
        }

        private class Entry
        {
            public DebugTextCategory category;
            public string display_text;
            public float end_time;
        }
    }
}