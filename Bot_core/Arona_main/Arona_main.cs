using System.Text.Json.Nodes;
using Bot_core;
using Lagrange.Core.Message;
using Plugins.Core;
using Tool_lib;

namespace Plugins;

public sealed class Arona_main:Base_plugin
{
    public bool __main__  => true;
    public Bot? bot { get; set; }
    private JsonObject help_document = new JsonObject();
    private Dictionary<string, Base_plugin> plugins = [];
    private Dictionary<string, Func<Plugin_message, Task>> reply_dict = [];
    public Arona_main() : base(3065613494, "Arona_main")
    {
        key_words = ["帮助","插件"];
        
        help_document["帮助"] = new JsonObject();
        help_document["帮助"]!["<无参数>"] = "查询当前所有已安装的插件,插件的帮助信息请通过参数查询";
        help_document["帮助"]!["[插件名称]"] = "查询特定插件的帮助信息";
        //help_document["账号"] = "查询账号的信息";
        help_document["插件"] = "查询当前所有已安装的插件";

        reply_dict["帮助"] = plugin_help;
        reply_dict["插件"] = get_plugins;
    }

    public override async Task reply(Plugin_message message)
    {
        await reply_dict[message.key_word](message);
    }

    public override Task update(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public override JsonObject help()
    {
        return help_document;
    }

    public override Task load_data()
    {
        if (bot != null)
        {
            plugins = bot.get_plugins();
            Console.WriteLine("Connect to bot successfully!!!");
        }
        else
        {
            Console.WriteLine("Failed to connect to bot!!!");
        }
        return Task.CompletedTask;
    }

    public override Task save_data()
    {
        return Task.CompletedTask;
    }

    private string generate_plugin_string()
    {
        return "什庭之匣已安装的插件:\n" + string.Join("\n", plugins.Keys);
    }

    private Task plugin_help(Plugin_message message)
    {
        var reply_message = message.group_id.HasValue?MessageBuilder.Group(message.group_id.Value):MessageBuilder.Friend(message.source_id);
        switch (message.args.Length)
        {
            case 0:
                reply_message.Text($"老师,你想查询哪个插件的帮助文档呢?\n{generate_plugin_string()}");
                break;
            case 1:
                reply_message.Text(plugins.TryGetValue(message.args[0], out var plugin)
                    ? $"{plugin}:\n{plugin.help().To_format_string()}"
                    : "老师,什庭之匣尚未安装该插件...");
                break;
            default:
                reply_message.Text($"老师,你传递了错误的参数: {string.Join(" ", message.args)}");
                break;
        }
        push_messages(reply_message);
        return Task.CompletedTask;
    }

    private Task get_plugins(Plugin_message message)
    {
        var reply_message = message.group_id.HasValue?MessageBuilder.Group(message.group_id.Value):MessageBuilder.Friend(message.source_id);
        reply_message.Text(generate_plugin_string());
        push_messages(reply_message);
        return Task.CompletedTask;
    }
}