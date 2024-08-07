using Bot_core;
using Tool_lib;

Functions_lib.initialize_path("QQbot-4.0");
var bot = new Bot(3644260939,3065613494);
bot.add_white_group(189833004);
if (await bot.log_in_by_password())
{
    Console.WriteLine("Bot has login!!!");
}

Console.CancelKeyPress += (_, _) =>
{
    bot.Dispose();
    Environment.Exit(0);
};

try
{
    bot.start();
}
catch (Exception e)
{
    Console.WriteLine(e);
    bot.Dispose();
}
