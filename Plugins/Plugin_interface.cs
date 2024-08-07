using System.Text.Json.Nodes;
using Lagrange.Core.Message;

namespace Plugins
{
    namespace Core
    {
        public delegate Base_plugin? get_plugin_function(string plugin_name);
        public delegate void push_message_function(params MessageBuilder[] messages);
        public delegate MessageBuilder[] create_all_group_message_function();

        public interface Plugin_interface
        {
            public uint master_id { get; }
            public string plugin_name { get; }
            public string plugin_file_path { get; }
            public int update_interval { get; }
            public HashSet<string> key_words { get; }
            public bool plugin_enabled { get; set; }
            public get_plugin_function get_plugin { get; set; }
            public push_message_function push_messages { get; set; }
            public create_all_group_message_function create_all_group_message { get; set; }
            public Task reply(Plugin_message message);
            public Task update(CancellationToken token);
            public Task load_data();
            public Task save_data();
            public JsonObject help();
        }

        public abstract class Base_plugin(uint master_ID, string Plugin_name, int interval = -1) : Plugin_interface
        {
            public uint master_id { get;} = master_ID;
            public string plugin_name { get;} = Plugin_name;
            public required string plugin_file_path { get; set; }
            public int update_interval { get;} = interval is < 0 or > 360 ? interval : 360;
            public HashSet<string> key_words { get; protected init; } = [];
            private volatile bool enabled = true;
            public bool plugin_enabled
            {
                get => enabled;
                set => enabled = value;
            }
            public required get_plugin_function get_plugin { get; set; }
            public required push_message_function push_messages { get; set; }
            public required create_all_group_message_function create_all_group_message { get; set; }
            public abstract Task reply(Plugin_message message);
            public abstract Task update(CancellationToken token);
            public abstract Task load_data();
            public abstract Task save_data();
            public abstract JsonObject help();

            public override int GetHashCode()
            {
                return plugin_name.GetHashCode();
            }

            public override bool Equals(object? obj)
            {
                return obj switch
                {
                    string str => plugin_name == str,
                    Base_plugin other => plugin_name == other.plugin_name,
                    _ => false
                };
            }

            public override string ToString()
            {
                return plugin_name;
            }
        }

        public record Plugin_message
        {
            public string key_word;
            public string[] args;
            public uint source_id;
            public uint? group_id;
            public MessageChain message_source;
        }
    }
}