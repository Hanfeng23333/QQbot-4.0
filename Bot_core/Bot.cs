using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Text.Json;
using Lagrange.Core;
using Lagrange.Core.Common;
using Lagrange.Core.Common.Interface;
using Lagrange.Core.Common.Interface.Api;
using Lagrange.Core.Event.EventArg;
using Lagrange.Core.Message;
using Plugins.Core;
using Tool_lib;

namespace Bot_core;

public class Bot
{
    private BotContext? bot;
    private BotDeviceInfo? device_info;
    private BotKeystore? key_store;
    
    private readonly uint account;
    private readonly uint master_account;
    private HashSet<uint> white_group = [];
    private readonly string bot_working_path;
    private readonly string bot_cache_path;
    private readonly string bot_plugin_path;

    private Dictionary<string,Base_plugin> plugins = [];
    private HashSet<Task> reply_task_pool = [];
    private Dictionary<string,(CancellationTokenSource source,Task update_task,Task timer_task)> update_task_pool = [];
    private HashSet<Task> data_task_pool = [];
    private HashSet<Task> send_task_pool = [];
    private ConcurrentQueue<MessageChain> received_message_queue = [];
    private ConcurrentQueue<MessageChain> send_message_queue = [];

    private volatile bool thread_active = true;
    private Thread? receive_thread;
    private Thread? send_thread;
        
    public Bot(uint bot_account,uint bot_master_account)
    {
        account = bot_account;
        master_account = bot_master_account;

        bot_working_path = Directory.GetCurrentDirectory();
        bot_cache_path = Path.Combine(bot_working_path, "bot_cache", account.ToString());
        bot_plugin_path = Path.Combine(bot_working_path, "Plugins");
        if (!Directory.Exists(bot_cache_path))
            Directory.CreateDirectory(bot_cache_path);
    }
    
    ~Bot()
    {
        Dispose();
    }

    public void Dispose()
    {
        thread_active = false;
        receive_thread?.Join();
        send_thread?.Join();
        if (bot == null) 
            return;
        if (save_info().Result)
            Console.WriteLine("save the bot info successfully!");
        bot.Dispose();
        Console.WriteLine("Bot has quited successfully!");
    }

    public async Task<bool> log_in_by_qrcode(string? device_name = null)
    {
        device_info = BotDeviceInfo.GenerateInfo();
        if(device_name != null)
            device_info.DeviceName = device_name;
        key_store = new BotKeystore();
        
        bot = BotFactory.Create(new BotConfig(),device_info,key_store);
        var qrcode = await bot.FetchQrCode();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(bot_cache_path,"qrcode.png"),qrcode.GetValueOrDefault().QrCode);
            Console.WriteLine("Get the qrcode successfully! Please scan it quickly!");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Failed to get the qrcode!!!\nError message:\n{exception.Message}");
            return false;
        }
        await bot.LoginByQrCode();
        await save_info();
        return true;
    }

    public async Task<bool> log_in_by_password()
    {
        var save_path = Path.Combine(bot_cache_path,"bot_info");
        if (!Directory.Exists(save_path))
        {
            Console.WriteLine("No bot info cache existed! Please log in by qrcode instead!");
            return false;
        }
        FileStream? device_file = null, key_file = null;
        try
        {
            device_file = new FileStream(Path.Combine(save_path,"device_info.device"), FileMode.Open,FileAccess.Read);
            key_file = new FileStream(Path.Combine(save_path,"key_store.key"), FileMode.Open,FileAccess.Read);
            var device_task = JsonSerializer.DeserializeAsync<BotDeviceInfo>(device_file);
            var key_task = JsonSerializer.DeserializeAsync<BotKeystore>(key_file);

            var device_tmp = await device_task;
            var key_tmp = await key_task;

            device_info = device_tmp ?? throw new NoNullAllowedException("Failed to load device info!!!");
            key_store = key_tmp ?? throw new NoNullAllowedException("Failed to load key store!!!");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Failed to load the bot info!!!\nError message:\n{exception.Message}");
            Console.WriteLine("Please log in by qrcode instead!");
            return false;
        }
        finally
        {
            if(device_file != null)
                await device_file.DisposeAsync();
            if(key_file != null)
                await key_file.DisposeAsync();
        }
        bot = BotFactory.Create(new BotConfig(),device_info,key_store);
        return await bot.LoginByPassword();
    }

    public async Task<bool> save_info()
    {
        if (bot == null)
            throw new NoNullAllowedException("Bot hasn't login yet!!!");
        
        device_info = bot.UpdateDeviceInfo();
        key_store = bot.UpdateKeystore();
        
        var save_path = Path.Combine(bot_cache_path,"bot_info");
        if (!Directory.Exists(save_path))
        { 
            Directory.CreateDirectory(save_path);
        }
        
        FileStream? device_file = null, key_file = null;
        try
        {
            device_file = new FileStream(Path.Combine(save_path,"device_info.device"), FileMode.OpenOrCreate,FileAccess.Write);
            key_file = new FileStream(Path.Combine(save_path,"key_store.key"), FileMode.OpenOrCreate,FileAccess.Write);
            var device_task = JsonSerializer.SerializeAsync(device_file, device_info);
            var key_task = JsonSerializer.SerializeAsync(key_file, key_store);
            Task.WaitAll(device_task,key_task);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Failed to save the bot info!!!\nError message:\n{exception.Message}");
            return false;
        }
        finally
        {
            if(device_file != null)
                await device_file.DisposeAsync();
            if(key_file != null)
                await key_file.DisposeAsync();
        }
        return true;
    }

    public void add_white_group(uint group_id)
    {
        white_group.Add(group_id);
    }

    public Base_plugin? get_plugin(string plugin_name) => plugins.GetValueOrDefault(plugin_name);
    public Dictionary<string, Base_plugin> get_plugins() => plugins;
    public MessageBuilder[] create_all_group_message() => white_group.Select(MessageBuilder.Group).ToArray();
    private void receive_group_message(object sender, GroupMessageEvent message_event)
    {
        received_message_queue.Enqueue(message_event.Chain);
    }

    private void receive_friend_message(object sender, FriendMessageEvent message_event)
    {
        received_message_queue.Enqueue(message_event.Chain);
    }

    public void update_plugin(string plugin_name)
    {
        var plugin = plugins.GetValueOrDefault(plugin_name);
        if(plugin == null || plugin.update_interval < 0)
            return;
        if (update_task_pool.Remove(plugin_name, out var value))
        {
            value.source.Cancel();
        }
        var source = new CancellationTokenSource();
        var update_task = plugin_function_wrapper(async () =>
        {
            await plugin.update(source.Token).WaitAsync(TimeSpan.FromMinutes(5), source.Token);
        }, "update", plugin);
        var timer_task = Task.Delay(TimeSpan.FromSeconds(plugin.update_interval), source.Token).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
                update_plugin(plugin_name);
        },CancellationToken.None);
        update_task_pool[plugin_name] = (source, update_task, timer_task);
    }

    public async Task remove_plugin(Base_plugin plugin,(string function_name,string error_msg)? error_info)
    {
        plugin.plugin_enabled = false;
        if (error_info != null)
        {
            foreach (var error_message in create_all_group_message())
            {
                error_message.Text($"An error occurred in the {error_info.Value.function_name} function of {plugin.plugin_name} Plugin!\nError message: {error_info.Value.error_msg}\n");
                send_message_queue.Enqueue(error_message.Build());
            }
        }
        if (update_task_pool.TryGetValue(plugin.plugin_name, out var value))
        {
            await value.source.CancelAsync();
            update_task_pool.Remove(plugin.plugin_name);
        }
        
        var save_message = $"Save the data of {plugin.plugin_name} Plugin successfully!";
        try
        {
            await plugin.save_data();
        }
        catch (Exception save_exception)
        {
            save_message =
                $"An error occurred in the save function of {plugin.plugin_name} Plugin!\nError message: {save_exception}\nFailed to save data of {plugin.plugin_name} Plugin!";
        }
        finally
        {
            foreach (var message in create_all_group_message())
            {
                message.Text(save_message);
                send_message_queue.Enqueue(message.Build());
            }
        }
        plugins.Remove(plugin.plugin_name);
        foreach (var remove_message in create_all_group_message())
        {
            remove_message.Text($"{plugin.plugin_name} has been removed!");
            send_message_queue.Enqueue(remove_message.Build());
        }
    }
    
    private async Task<bool> plugin_function_wrapper(Func<Task> plugin_function,string function_name,Base_plugin plugin)
    {
        try
        {
            await plugin_function();
            return true;
        }
        catch (AggregateException exception)
        {
            await remove_plugin(plugin, (function_name, exception.InnerExceptions.Count > 0 ? string.Join("\n",exception.InnerExceptions.Select(e => e.ToString())) : "Unknown error!"));
        }
        catch (Exception exception)
        {
            if (exception is not TaskCanceledException)
            {
                await remove_plugin(plugin, (function_name, exception.ToString()));
            }
        }
        return false;
    }

    private void receive_thread_function()
    {
        while (thread_active)
        {
            while (received_message_queue.TryDequeue(out var message_chain))
            {
                if (message_chain.FriendUin != master_account && !white_group.Contains(message_chain.GroupUin.GetValueOrDefault(0))) 
                    continue;
                var plugin_message = Functions_lib.generate_plugin_message(message_chain, account);
                if(plugin_message == null)
                    continue;
                foreach (var plugin in plugins.Values.Where(plugin => plugin.plugin_enabled && plugin.key_words.Contains(plugin_message.key_word)))
                {
                    var reply_task = plugin_function_wrapper(async () =>
                    {
                        await plugin.reply(plugin_message);
                    }, "reply", plugin);
                    reply_task_pool.Add(reply_task);
                    reply_task.ContinueWith(reply_task_pool.Remove);
                }
            }
            Thread.Sleep(TimeSpan.FromMicroseconds(50));
        }
    }

    private void send_thread_function()
    {
        while (thread_active)
        {
            while (send_message_queue.TryDequeue(out var message_chain))
            {
                var send_task = bot?.SendMessage(message_chain);
                if(send_task == null)
                    continue;
                send_task_pool.Add(send_task);
                send_task.ContinueWith(send_task_pool.Remove);
            }
            Thread.Sleep(TimeSpan.FromMicroseconds(50));
        }
    }

    public void start()
    {
        if (bot == null)
            throw new NoNullAllowedException("Bot hasn't login yet!!!");
        
        foreach (var plugin_class in Assembly.Load("Plugins").GetTypes().Concat(Assembly.Load("Bot_core").GetTypes()).Where(type => type is { Namespace: "Plugins", IsClass: true, IsAbstract: false, IsPublic: true}))
        {
            if (Activator.CreateInstance(plugin_class) is not Base_plugin plugin) 
                continue;
            var plugin_type = plugin.GetType();
            var main_plugin_check = plugin_type.GetProperty("__main__");
            if (main_plugin_check?.PropertyType == typeof(bool) && (bool)main_plugin_check.GetValue(plugin)!)
            {
                var bot_interface = plugin_type.GetProperty("bot");
                if (bot_interface?.PropertyType == typeof(Bot) && bot_interface.CanWrite)
                {
                    bot_interface.SetValue(plugin,this);
                }
            }

            plugin.get_plugin = get_plugin;
            plugin.create_all_group_message = create_all_group_message;
            plugin.push_messages = (params MessageBuilder[] messages) =>
            {
                foreach (var message in messages)
                {
                    message.Text($"\n—— by {plugin.plugin_name} plugin");
                    send_message_queue.Enqueue(message.Build());
                }
            };
            plugin.plugin_file_path = Path.Combine(bot_plugin_path,"Plugins_data",plugin.plugin_name);
            if (!Directory.Exists(plugin.plugin_file_path))
                Directory.CreateDirectory(plugin.plugin_file_path);
            plugins.Add(plugin.plugin_name,plugin);

            var load_task = plugin_function_wrapper(plugin.load_data,"load data",plugin);
            data_task_pool.Add(load_task);
            plugin.plugin_enabled = false;
            load_task.ContinueWith(task =>
            {
                data_task_pool.Remove(task);
                update_plugin(plugin.plugin_name);
                plugin.plugin_enabled = true;
            });
        }
        Console.WriteLine("Plugins Loaded:");
        Console.WriteLine("----------------");
        Console.WriteLine(string.Join("\n",plugins.Keys));
        Console.WriteLine("----------------");

        bot.Invoker.OnGroupMessageReceived += receive_group_message;
        bot.Invoker.OnFriendMessageReceived += receive_friend_message;

        receive_thread = new Thread(receive_thread_function);
        send_thread = new Thread(send_thread_function);
        receive_thread.Start();
        send_thread.Start();
        
        Console.WriteLine("Bot has started...");
    }
}