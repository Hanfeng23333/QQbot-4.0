using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private JsonObject help_document = new();
    private Dictionary<string, Base_plugin> plugins = [];
    private Dictionary<string, Func<Plugin_message, Task>> reply_dict = [];
    private Dictionary<string, Func<Plugin_message, Task>> master_command_dict = [];
    private Dictionary<uint, User> user_data = [];
    private Converter currency_converter = new(1000);
    
    [Serializable] 
    private class User
    {
        public long credit { get; set; } = 0;
        public long stone { get; set; } = 0;
        public DateOnly? last_sign { get; set; } = null;
        public long sign_days { get; set; } = 0;

        public override string ToString()
        {
            return $"信用点: {credit}\n青辉石: {stone}\n上次签到日期: {(last_sign != null ? last_sign : "尚未进行签到")}\n累计签到时长: {sign_days}";
        }
    }

    private class Converter(long stone_to_credit)
    {
        private long stone_to_credit { get; set; } = stone_to_credit;

        public long convert_to_credit(long stone)
        {
            return stone * stone_to_credit;
        }

        public long convert_to_stone(long credit)
        {
            return credit / stone_to_credit;
        }

        public override string ToString()
        {
            return $"1 青辉石 <=> {convert_to_credit(1)} 信用点";
        }

        public string ToCreditString()
        {
            return $"1 青辉石 -> {convert_to_credit(1)} 信用点";
        }

        public string ToStoneString()
        {
            return $"{convert_to_credit(1)} 信用点 -> 1 青辉石";
        }
    }
    public Arona_main() : base(3065613494, "Arona_main")
    {
        key_words = ["帮助","插件","账号","签到","转换","氪金"];
        
        help_document["帮助"] = new JsonObject();
        help_document["帮助"]!["<无参数>"] = "查询当前所有已安装的插件,插件的帮助信息请通过参数查询";
        help_document["帮助"]!["[插件名称]"] = "查询特定插件的帮助信息";
        help_document["插件"] = "查询当前所有已安装的插件";
        help_document["账号"] = "查询账号的信息";
        help_document["签到"] = "进行当日签到";
        help_document["转换"] = new JsonObject();
        help_document["转换"]!["<无参数>"] = "查看青辉石和信用点的汇率(向下取整)";
        help_document["转换"]!["信用点"] = new JsonObject();
        help_document["转换"]!["信用点"]!["<无参数>"] = "查看青辉石转换成信用点的汇率(向下取整)";
        help_document["转换"]!["信用点"]!["[转换额度(整数)]"] = "将[转换额度(整数)]的青辉石转换为信用点(向下取整)";
        help_document["转换"]!["青辉石"] = new JsonObject();
        help_document["转换"]!["青辉石"]!["<无参数>"] = "查看信用点转换成青辉石的汇率(向下取整)";
        help_document["转换"]!["青辉石"]!["[转换额度(整数)]"] = "将[转换额度(整数)]的信用点转换为青辉石(向下取整)";

        reply_dict["帮助"] = plugin_help;
        reply_dict["插件"] = get_plugins;
        reply_dict["账号"] = get_account;
        reply_dict["签到"] = sign_in;
        reply_dict["转换"] = convert_credit_or_stone;

        master_command_dict["氪金"] = recharge;
    }

    public override async Task reply(Plugin_message message)
    {
        if (reply_dict.TryGetValue(message.key_word, out var reply_func))
        {
            await reply_func(message);
        }
        else if(message.source_id == master_id && master_command_dict.TryGetValue(message.key_word,out var master_func))
        {
            await master_func(message);
        }
    }

    public override Task update(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public override JsonObject help()
    {
        return help_document;
    }

    public override async Task load_data()
    {
        if (bot != null)
        {
            plugins = bot.get_plugins();
            Console.WriteLine("Arona_main has connected to the bot!");
        }
        else
        {
            Console.WriteLine("Arona_main failed to connect to bot!!!");
        }

        var data_path = Path.Combine(plugin_file_path, "user_data.data");
        if (!File.Exists(data_path))
        {
            await File.WriteAllTextAsync(data_path, "{}");
        }
        
        var load_stream = new FileStream(data_path, FileMode.Open, FileAccess.Read,FileShare.Inheritable);
        var data = await JsonSerializer.DeserializeAsync<Dictionary<uint, User>>(load_stream);
        if (data != null)
        {
            user_data = data;
            Console.WriteLine("Arona_main loaded data successfully!");
        }
        else
        {
            Console.WriteLine("Arona_main failed to load data!");
        }
        await load_stream.DisposeAsync();
    }

    public override async Task save_data()
    {
        var save_stream = new FileStream(Path.Combine(plugin_file_path,"user_data.data"), FileMode.OpenOrCreate, FileAccess.Write,FileShare.Inheritable);
        await JsonSerializer.SerializeAsync(save_stream,user_data);
        await save_stream.DisposeAsync();
    }

    private string generate_plugin_string()
    {
        return "什庭之匣已安装的插件:\n" + string.Join("\n", plugins.Keys);
    }

    private void check_user_is_existed(uint account)
    {
        user_data.TryAdd(account,new User());
    }

    private Task plugin_help(Plugin_message message)
    {
        var reply_message = message.group_id.HasValue?MessageBuilder.Group(message.group_id.Value):MessageBuilder.Friend(message.source_id);
        switch (message.args)
        {
            case []:
                reply_message.Text($"老师,你想查询哪个插件的帮助文档呢?\n{generate_plugin_string()}");
                break;
            case [string name]:
                reply_message.Text(plugins.TryGetValue(name, out var plugin)
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
        reply_message.Text(message.args.Length == 0?generate_plugin_string():$"老师,你传递了错误的参数: {string.Join(" ", message.args)}");
        push_messages(reply_message);
        return Task.CompletedTask;
    }

    private Task get_account(Plugin_message message)
    {
        var reply_message = message.group_id.HasValue?MessageBuilder.Group(message.group_id.Value):MessageBuilder.Friend(message.source_id);
        check_user_is_existed(message.source_id);
        reply_message.Mention(message.source_id);
        reply_message.Text(message.args.Length == 0
            ? $"老师的账号信息为: \n{user_data[message.source_id]}"
            : $"老师,你传递了错误的参数: {string.Join(" ", message.args)}");
        push_messages(reply_message);
        return Task.CompletedTask;
    }

    private Task sign_in(Plugin_message message)
    {
        var reply_message = message.group_id.HasValue?MessageBuilder.Group(message.group_id.Value):MessageBuilder.Friend(message.source_id);
        check_user_is_existed(message.source_id);
        reply_message.Mention(message.source_id);
        if (message.args.Length == 0)
        {
            var user = user_data[message.source_id];
            long stone;
            if (user.last_sign != null)
            {
                switch ((DateTime.Today - user.last_sign.Value.ToDateTime(TimeOnly.MinValue)).Days)
                {
                    case 0:
                        reply_message.Text($"老师,你今天已经签到过了哦!\n");
                        break;
                    case 1:
                        user.stone += stone = Math.Min(++user.sign_days * 100, 2000);
                        reply_message.Text($"老师,签到成功!\n本次签到老师获得{stone}青辉石\n");
                        break;
                    default:
                        user.stone += stone = (user.sign_days = 1) * 100;
                        reply_message.Text($"老师,签到成功!\n本次签到老师获得{stone}青辉石\n");
                        break;
                }
            }
            else
            {
                user.stone += stone = (user.sign_days = 1) * 100;
                reply_message.Text($"老师,签到成功!\n本次签到老师获得{stone}青辉石\n");
            }
            user.last_sign = DateOnly.FromDateTime(DateTime.Today);
            reply_message.Text($"老师已累计签到{user.sign_days}天,继续保持!");
        }
        else
        {
            reply_message.Text($"老师,你传递了错误的参数: {string.Join(" ", message.args)}");
        }

        push_messages(reply_message);
        return Task.CompletedTask;
    }

    private Task convert_credit_or_stone(Plugin_message message)
    {
        var reply_message = message.group_id.HasValue?MessageBuilder.Group(message.group_id.Value):MessageBuilder.Friend(message.source_id);
        check_user_is_existed(message.source_id);
        var user = user_data[message.source_id];
        reply_message.Mention(message.source_id);
        switch (message.args)
        {
            case []:
                reply_message.Text($"老师,青辉石和信用点转换汇率(向下取整)为:\n {currency_converter}");
                break;
            case ["信用点"]:
                reply_message.Text($"老师,信用点的汇率(向下取整)为:\n {currency_converter.ToCreditString()}");
                break;
            case ["信用点",string number]:
                if (long.TryParse(number, out var stone))
                {
                    if (user.stone >= stone)
                    {
                        var num = currency_converter.convert_to_credit(stone);
                        user.credit += num;
                        user.stone -= stone;
                        reply_message.Text($"老师,已成功将{stone}青辉石兑换为{num}信用点");
                    }
                    else
                    {
                        reply_message.Text("老师,你没有足够的青辉石来兑换成信用点!");
                    }
                }
                else
                {
                    reply_message.Text($"老师,{number}不是一个有效的整数!");
                }
                break;
            case ["青辉石"]:
                reply_message.Text($"老师,信用点的汇率(向下取整)为:\n {currency_converter.ToStoneString()}");
                break;
            case ["青辉石",string number]:
                if (long.TryParse(number, out var credit))
                {
                    if (user.credit >= credit)
                    {
                        var num = currency_converter.convert_to_stone(credit);
                        user.stone += num;
                        user.credit -= credit;
                        reply_message.Text($"老师,已成功将{credit}信用点兑换为{num}青辉石");
                    }
                    else
                    {
                        reply_message.Text("老师,你没有足够的信用点来兑换成青辉石!");
                    }
                }
                else
                {
                    reply_message.Text($"老师,{number}不是一个有效的整数!");
                }
                break;
            default:
                reply_message.Text($"老师,你传递了错误的参数: {string.Join(" ", message.args)}");
                break;
        }

        push_messages(reply_message);
        return Task.CompletedTask;
    }

    private Task recharge(Plugin_message message)
    {
        var reply_message = message.group_id.HasValue?MessageBuilder.Group(message.group_id.Value):MessageBuilder.Friend(message.source_id);
        var target = message.target_id ?? master_id;
        check_user_is_existed(target);
        if (message.args is [string number])
        {
            if (long.TryParse(number, out var stone))
            {
                user_data[target].stone += stone;
                reply_message.Text("老师,已成功为").Mention(target).Text($"老师充值{stone}青辉石!");
            }
            else
            {
                reply_message.Text($"老师,{number}不是一个有效的整数!");
            }
        }
        else
        {
            reply_message.Text($"老师,你传递了错误的参数: {string.Join(" ", message.args)}");
        }
        push_messages(reply_message);
        return Task.CompletedTask;
    }
}