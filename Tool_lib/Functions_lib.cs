using System.Text.Json;
using System.Text.Json.Nodes;
using Lagrange.Core.Message;
using Lagrange.Core.Message.Entity;
using Plugins.Core;

namespace Tool_lib;

public static class Functions_lib
{
    public static bool initialize_path(string name)
    {
        var current_path = Directory.GetCurrentDirectory();
        var working_path_index = current_path.IndexOf(name,StringComparison.Ordinal);
        if (working_path_index == -1)
        {
            Console.WriteLine("Failed to find the project directory!");
            return false;
        }
        Directory.SetCurrentDirectory(current_path.Remove(working_path_index+name.Length));
        Console.WriteLine("Working path has initialize successfully!");
        return true;
    }
    public static Plugin_message? generate_plugin_message(MessageChain message_chain,uint bot_account)
    {
        if (message_chain.FriendUin == bot_account)
            return null;
        var message = new Plugin_message();
        switch (message_chain)
        {
            case [TextEntity text_entity]:
                (message.key_word,message.args) = parse(text_entity.Text);
                break;
            case [MentionEntity mention_entity,TextEntity text_entity]:
                (message.key_word,message.args) = parse(text_entity.Text,mention_entity.Uin == bot_account);
                message.target_id = mention_entity.Uin;
                break;
            case [ForwardEntity,MentionEntity mention_entity,TextEntity text_entity]:
                (message.key_word,message.args) = parse(text_entity.Text,mention_entity.Uin == bot_account);
                message.target_id = mention_entity.Uin;
                break;
            default:
                return null;
        }

        if (string.IsNullOrEmpty(message.key_word))
            return null;
        message.source_id = message_chain.FriendUin;
        message.group_id = message_chain.GroupUin;
        message.message_source = message_chain;
        return message;
    }

    public static (string, string[]) parse(string text,bool ignore_bias = false)
    {
        var buffer = new List<string>(text.Trim().Split(" "));
        buffer.RemoveAll(string.IsNullOrEmpty);
        if (buffer.Count == 0)
            return ("",[]);
        var key = buffer[0];
        if (key[0] == '/')
        {
            key = key.Substring(1);
            ignore_bias = true;
        }
        if (!ignore_bias)
            return ("",[]);
        buffer.RemoveAt(0);
        return (key, buffer.Count > 0 ? buffer.ToArray() : []);
    }

    public static string To_format_string(this JsonObject json_object,int indent = 0,string forward_string = "")
    {
        var string_lines = new List<string>();
        foreach (var (key, value) in json_object)
        {
            var key_string = $"{new string(' ',2*indent)}{forward_string}{key}:";
            if (value!.GetValueKind() == JsonValueKind.Object)
            {
                string_lines.Add(key_string);
                string_lines.Add(value.AsObject().To_format_string(indent+1,forward_string+$"{key} "));
            }
            else
            {
                string_lines.Add($"{key_string} {value.GetValue<string>()}");
            }
        }
        return string.Join("\n", string_lines);
    }
}